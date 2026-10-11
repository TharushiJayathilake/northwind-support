import { useRef, useState } from 'react';
import { ApiError, toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { Field } from '../../../components/Field';
import type { EscalateTicketResponse, TicketDetail } from '../../../types/api';

interface EscalationFormProps {
  ticket?: Pick<TicketDetail, 'id' | 'priority' | 'status'> | null;
  onEscalated: (response: EscalateTicketResponse) => void | Promise<void>;
  onConflict: () => void | Promise<void>;
}

export function EscalationForm({ ticket, onEscalated, onConflict }: EscalationFormProps) {
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string>();
  const [success, setSuccess] = useState<string>();
  const [isSaving, setIsSaving] = useState(false);
  const pending = useRef(false);
  const unavailableReason = !ticket || !Number.isInteger(ticket.id) || ticket.id <= 0 ||
    !['Low', 'Medium', 'High', 'Critical'].includes(ticket.priority) ||
    !['New', 'Open', 'InProgress', 'Resolved', 'Closed'].includes(ticket.status)
    ? 'Escalation is unavailable until ticket details are loaded.'
    : ticket.status === 'Resolved' || ticket.status === 'Closed'
      ? `Escalation is unavailable because this ticket is ${ticket.status}.`
      : ticket.priority === 'Critical'
        ? 'Escalation is unavailable because this ticket is already Critical.'
        : undefined;
  const disabled = isSaving || unavailableReason !== undefined;

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (pending.current || unavailableReason || !ticket) return;
    const validation: Record<string, string> = {};
    if (reason.trim().length < 5 || reason.length > 500)
      validation.reason = 'Enter a reason of 5-500 characters.';
    setErrors(validation);
    setError(undefined);
    setSuccess(undefined);
    if (Object.keys(validation).length > 0) return;

    pending.current = true;
    setIsSaving(true);
    try {
      const response = await ticketsApi.escalateTicket(ticket.id, { reason: reason.trim() });
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
      if (caught instanceof ApiError && caught.status === 409) {
        try {
          await onConflict();
        } catch {
          setError(`${toErrorMessage(caught, 'Could not escalate the ticket.')} Could not refresh ticket details. Please retry refreshing the ticket.`);
        }
      }
    } finally {
      pending.current = false;
      setIsSaving(false);
    }
  };

  return (
    <form className="card form" onSubmit={submit} noValidate aria-label="Escalate ticket">
      <h2>Escalate ticket</h2>
      {unavailableReason && <p id="escalation-unavailable">{unavailableReason}</p>}
      <Field id="escalation-reason" label="Reason" error={errors.reason}>
        {(props) => <textarea {...props} rows={4} value={reason} disabled={disabled}
          onChange={(event) => { setReason(event.target.value); setSuccess(undefined); }} />}
      </Field>
      {error && <p className="field__error" role="alert">{error}</p>}
      {success && <p role="status">{success}</p>}
      <div className="button-row">
        <button type="submit" className="button button--primary" disabled={disabled}
          aria-describedby={unavailableReason ? 'escalation-unavailable' : undefined}>
          {isSaving ? 'Escalating...' : 'Escalate ticket'}
        </button>
      </div>
    </form>
  );
}
