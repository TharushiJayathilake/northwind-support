import { useState } from 'react';
import { ApiError, toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { Field } from '../../../components/Field';
import type {
  Category,
  CreateTicketPayload,
  CustomerListItem,
  TicketDetail,
  TicketPriority,
} from '../../../types/api';

const priorities: TicketPriority[] = ['Low', 'Medium', 'High', 'Critical'];

interface CreateTicketFormProps {
  customers: CustomerListItem[];
  categories: Category[];
  onCreated: (ticket: TicketDetail) => void;
}

interface FormState {
  title: string;
  description: string;
  customerId: string;
  categoryId: string;
  requestedPriority: TicketPriority | '';
}

const emptyForm: FormState = {
  title: '',
  description: '',
  customerId: '',
  categoryId: '',
  requestedPriority: '',
};

/** Raises a ticket. Validates the same rules as the API before calling it. */
export function CreateTicketForm({ customers, categories, onCreated }: CreateTicketFormProps) {
  const [form, setForm] = useState<FormState>(emptyForm);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [isSaving, setIsSaving] = useState(false);
  const [submitError, setSubmitError] = useState<string>();

  const update = (patch: Partial<FormState>) => setForm((current) => ({ ...current, ...patch }));

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const validationErrors = validate(form);
    setErrors(validationErrors);
    setSubmitError(undefined);

    if (Object.keys(validationErrors).length > 0) {
      return;
    }

    const payload: CreateTicketPayload = {
      title: form.title.trim(),
      description: form.description.trim(),
      customerId: Number(form.customerId),
      categoryId: Number(form.categoryId),
      requestedPriority: form.requestedPriority === '' ? undefined : form.requestedPriority,
    };

    setIsSaving(true);

    try {
      const created = await ticketsApi.createTicket(payload);
      setForm(emptyForm);
      onCreated(created);
    } catch (caught) {
      if (caught instanceof ApiError && Object.keys(caught.fieldErrors).length > 0) {
        setErrors(fromApiErrors(caught.fieldErrors));
      }

      setSubmitError(toErrorMessage(caught, 'Could not raise the ticket.'));
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <form className="card form" onSubmit={submit} noValidate>
      <Field id="ticket-title" label="Title" error={errors.title} hint="At least 5 characters.">
        {(fieldProps) => (
          <input
            {...fieldProps}
            type="text"
            value={form.title}
            maxLength={200}
            onChange={(event) => update({ title: event.target.value })}
          />
        )}
      </Field>

      <Field
        id="ticket-description"
        label="Description"
        error={errors.description}
        hint="At least 10 characters. What happened, and since when?"
      >
        {(fieldProps) => (
          <textarea
            {...fieldProps}
            rows={5}
            value={form.description}
            maxLength={4000}
            onChange={(event) => update({ description: event.target.value })}
          />
        )}
      </Field>

      <Field id="ticket-customer" label="Customer" error={errors.customerId}>
        {(fieldProps) => (
          <select
            {...fieldProps}
            value={form.customerId}
            onChange={(event) => update({ customerId: event.target.value })}
          >
            <option value="">Select a customer</option>
            {customers.map((customer) => (
              <option key={customer.id} value={customer.id}>
                {customer.name} ({customer.tier})
              </option>
            ))}
          </select>
        )}
      </Field>

      <Field id="ticket-category" label="Category" error={errors.categoryId}>
        {(fieldProps) => (
          <select
            {...fieldProps}
            value={form.categoryId}
            onChange={(event) => update({ categoryId: event.target.value })}
          >
            <option value="">Select a category</option>
            {categories
              .filter((category) => category.isActive)
              .map((category) => (
                <option key={category.id} value={category.id}>
                  {category.name}
                </option>
              ))}
          </select>
        )}
      </Field>

      <Field
        id="ticket-priority"
        label="Requested priority"
        hint="Optional. The team may adjust it."
      >
        {(fieldProps) => (
          <select
            {...fieldProps}
            value={form.requestedPriority}
            onChange={(event) =>
              update({ requestedPriority: priorities.find((priority) => priority === event.target.value) ?? '' })
            }
          >
            <option value="">No preference</option>
            {priorities.map((priority) => (
              <option key={priority} value={priority}>
                {priority}
              </option>
            ))}
          </select>
        )}
      </Field>

      {submitError && (
        <p className="field__error" role="alert">
          {submitError}
        </p>
      )}

      <div className="button-row">
        <button type="submit" className="button button--primary" disabled={isSaving}>
          {isSaving ? 'Raising ticket...' : 'Raise ticket'}
        </button>
      </div>
    </form>
  );
}

function validate(form: FormState): Record<string, string> {
  const errors: Record<string, string> = {};

  if (form.title.trim().length < 5) {
    errors.title = 'Give the ticket a title of at least 5 characters.';
  }

  if (form.description.trim().length < 10) {
    errors.description = 'Describe the problem in at least 10 characters.';
  }

  if (form.customerId === '') {
    errors.customerId = 'Choose the customer this ticket is for.';
  }

  if (form.categoryId === '') {
    errors.categoryId = 'Choose a category.';
  }

  return errors;
}

/** Flattens the API's per-field messages into the one message per field the form shows. */
function fromApiErrors(fieldErrors: Record<string, string[]>): Record<string, string> {
  return Object.fromEntries(
    Object.entries(fieldErrors).map(([field, messages]) => [
      field.charAt(0).toLowerCase() + field.slice(1),
      messages[0],
    ]),
  );
}
