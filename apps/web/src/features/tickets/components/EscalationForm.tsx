import { useRef, useState } from 'react';
import { ApiError, toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { Field } from '../../../components/Field';
import type { EscalateTicketResponse } from '../../../types/api';

interface EscalationFormProps {
  ticketId: number;
  onEscalated: (response: EscalateTicketResponse) => void | Promise<void>;
}

export function EscalationForm({ ticketId, onEscalated }: EscalationFormProps) {
  const [reason, setReason] = useState('');
  const [actor, setActor] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string>();
  const [success, setSuccess] = useState<string>();
  const [isSaving, setIsSaving] = useState(false);
  const pending = useRef(false);

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (pending.current) return;
    const validation: Record<string, string> = {};
    if (reason.trim().length < 5 || reason.length > 500)
      validation.reason = 'Enter a reason of 5-500 characters.';
    if (actor.trim().length === 0 || actor.length > 100)
      validation.escalatedBy = 'Enter an actor name of 1-100 characters.';
    setErrors(validation);
    setError(undefined);
    setSuccess(undefined);
    if (Object.keys(validation).length > 0) return;

    pending.current = true;
    setIsSaving(true);
    try {
      const response = await ticketsApi.escalateTicket(ticketId, { reason: reason.trim(), escalatedBy: actor.trim() });
      setSuccess('Ticket escalated successfully.');
      setReason('');
      try {
        await onEscalated(response);
      } catch (refreshError) {
        setError(toErrorMessage(refreshError, 'Escalation succeeded, but refreshing the ticket failed.'));
      }
    } catch (caught) {
      if (caught instanceof ApiError) {
        setErrors(Object.fromEntries(Object.entries(caught.fieldErrors).map(([field, messages]) =>
          [field.charAt(0).toLowerCase() + field.slice(1), messages.join(' ')])));
      }
      setError(toErrorMessage(caught, 'Could not escalate the ticket.'));
    } finally {
      pending.current = false;
      setIsSaving(false);
    }
  };

  return (
    <form className="card form" onSubmit={submit} noValidate aria-label="Escalate ticket">
      <h2>Escalate ticket</h2>
      <Field id="escalation-reason" label="Reason" error={errors.reason}>
        {(props) => <textarea {...props} rows={4} value={reason} disabled={isSaving}
          onChange={(event) => { setReason(event.target.value); setSuccess(undefined); }} />}
      </Field>
      <Field id="escalation-actor" label="Escalated by (temporary)" error={errors.escalatedBy}>
        {(props) => <input {...props} type="text" value={actor} disabled={isSaving}
          onChange={(event) => { setActor(event.target.value); setSuccess(undefined); }} />}
      </Field>
      {error && <p className="field__error" role="alert">{error}</p>}
      {success && <p role="status">{success}</p>}
      <div className="button-row">
        <button type="submit" className="button button--primary" disabled={isSaving}>
          {isSaving ? 'Escalating...' : 'Escalate ticket'}
        </button>
      </div>
    </form>
  );
}
