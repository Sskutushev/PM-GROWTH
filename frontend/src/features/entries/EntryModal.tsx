import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Field, Form, Formik } from "formik";
import * as yup from "yup";
import { api, type Entry, type Lookup } from "../../api";
import { ErrorBanner } from "../../components/ErrorBanner";
import { Modal } from "../../components/Modal";
import { firstDayOf } from "./month";

export type FormValues = {
  employeeId: string;
  projectId: string;
  date: string;
  hours: number | string;
  comment: string;
};

// Mirrors the server-side rules so the obvious mistakes never leave the browser. The server
// still validates: this form is a convenience, not the authority.
const schema = yup.object({
  employeeId: yup.string().required("Выберите сотрудника"),
  projectId: yup.string().required("Выберите проект"),
  date: yup.string().required("Укажите дату"),
  hours: yup
    .number()
    .typeError("Укажите часы числом")
    .positive("Часы должны быть больше нуля")
    // The daily cap spans every entry of that day and only the server can check it. This one
    // is the rule about a single entry, and the wording has to say so.
    .max(24, "В одной записи нельзя указать больше 24 часов")
    .test(
      "step",
      "Шаг — 0,5 часа",
      (value) => value != null && value % 0.5 === 0,
    ),
  comment: yup.string().max(300, "Не длиннее 300 символов"),
});

export function EntryModal({
  entry,
  employees,
  projects,
  month,
  onClose,
}: {
  entry: Entry | null;
  employees: Lookup[];
  projects: Lookup[];
  month: string;
  onClose: () => void;
}) {
  const client = useQueryClient();

  const mutation = useMutation({
    mutationFn: (values: FormValues) =>
      api.save(entry?.id, {
        ...values,
        hours: Number(values.hours),
        version: entry?.version,
      }),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ["entries"] });
      onClose();
    },
  });

  return (
    <Modal
      eyebrow="Рабочее время"
      title={entry ? "Изменить запись" : "Новая запись"}
      onClose={onClose}
    >
      <Formik<FormValues>
        initialValues={{
          employeeId: entry?.employeeId ?? "",
          projectId: entry?.projectId ?? "",
          date: entry?.date ?? firstDayOf(month),
          hours: entry?.hours ?? 8,
          comment: entry?.comment ?? "",
        }}
        validationSchema={schema}
        onSubmit={(values) => mutation.mutate(values)}
      >
        {({ errors, touched }) => (
          <Form>
            <div className="form-grid">
              <FormField
                label="Сотрудник"
                name="employeeId"
                as="select"
                error={touched.employeeId && errors.employeeId}
              >
                <option value="">Выберите</option>
                {employees.map((employee) => (
                  <option key={employee.id} value={employee.id}>
                    {employee.name}
                  </option>
                ))}
              </FormField>
              <FormField
                label="Проект"
                name="projectId"
                as="select"
                error={touched.projectId && errors.projectId}
              >
                <option value="">Выберите</option>
                {projects.map((project) => (
                  <option key={project.id} value={project.id}>
                    {project.code} · {project.name}
                  </option>
                ))}
              </FormField>
              <FormField
                label="Дата"
                name="date"
                type="date"
                error={touched.date && errors.date}
              />
              <FormField
                label="Часы"
                name="hours"
                type="number"
                step="0.5"
                error={touched.hours && errors.hours}
              />
              <label className="wide">
                Комментарий
                <Field as="textarea" name="comment" rows={3} />
              </label>
            </div>
            {mutation.error && <ErrorBanner error={mutation.error} />}
            <div className="actions">
              <button type="button" onClick={onClose}>
                Отмена
              </button>
              <button
                className="primary"
                type="submit"
                disabled={mutation.isPending}
              >
                {mutation.isPending ? "Сохраняем…" : "Сохранить"}
              </button>
            </div>
          </Form>
        )}
      </Formik>
    </Modal>
  );
}

type FormFieldProps = {
  label: string;
  name: keyof FormValues;
  error?: string | false;
  children?: React.ReactNode;
  as?: "select" | "textarea";
  type?: "date" | "number" | "text";
  step?: string;
};

function FormField({ label, error, children, ...props }: FormFieldProps) {
  return (
    <label>
      {label}
      <Field {...props} aria-invalid={Boolean(error)}>
        {children}
      </Field>
      {error && <small className="field-error">{error}</small>}
    </label>
  );
}
