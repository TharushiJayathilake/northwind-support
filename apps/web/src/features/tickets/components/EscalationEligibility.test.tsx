import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../../api/tickets';
import { ticketDetail } from '../../../test/fixtures';
import type { TicketDetail, TicketPriority, TicketStatus } from '../../../types/api';
import { EscalationForm } from './EscalationForm';

vi.mock('../../../api/tickets', () => ({ ticketsApi: { escalateTicket: vi.fn() } }));
const priorities: TicketPriority[] = ['Low', 'Medium', 'High'];
const statuses: TicketStatus[] = ['New', 'Open', 'InProgress'];
const onEscalated = vi.fn();
const onConflict = vi.fn();
afterEach(() => vi.resetAllMocks());

describe('Escalation eligibility', () => {
  it.each(priorities.flatMap((priority) => statuses.map((status) => ({ priority, status }))))(
    'allows $priority tickets in $status status', (values) => {
      render(<EscalationForm ticket={ticketDetail(values)} onEscalated={onEscalated} onConflict={onConflict} />);
      expect(screen.getByRole('button', { name: 'Escalate ticket' })).toBeEnabled();
      expect(screen.getByLabelText('Reason')).toBeEnabled();
    });

  it.each([
    { ticket: ticketDetail({ priority: 'Critical' }), message: 'already Critical' },
    { ticket: ticketDetail({ status: 'Resolved' }), message: 'Resolved' },
    { ticket: ticketDetail({ status: 'Closed' }), message: 'Closed' },
    { ticket: undefined, message: 'until ticket details are loaded' },
    { ticket: null, message: 'until ticket details are loaded' },
    { ticket: ticketDetail({ id: NaN }), message: 'until ticket details are loaded' },
  ])('disables unavailable ticket controls: $message', async ({ ticket, message }) => {
    render(<EscalationForm ticket={ticket} onEscalated={onEscalated} onConflict={onConflict} />);
    const button = screen.getByRole('button', { name: 'Escalate ticket' });
    expect(button).toBeDisabled();
    expect(button).toHaveAccessibleDescription(expect.stringContaining(message));
    expect(screen.getByLabelText('Reason')).toBeDisabled();
    await act(async () => { fireEvent.submit(screen.getByRole('form', { name: 'Escalate ticket' })); });
    expect(ticketsApi.escalateTicket).not.toHaveBeenCalled();
  });

  it('reevaluates when priority or status changes in either direction', () => {
    const form = (ticket: TicketDetail) => <EscalationForm ticket={ticket}
      onEscalated={onEscalated} onConflict={onConflict} />;
    const { rerender } = render(form(ticketDetail()));
    rerender(form(ticketDetail({ priority: 'Critical' })));
    expect(screen.getByRole('button', { name: 'Escalate ticket' })).toBeDisabled();
    rerender(form(ticketDetail({ priority: 'Low' })));
    expect(screen.getByRole('button', { name: 'Escalate ticket' })).toBeEnabled();
    rerender(form(ticketDetail({ status: 'Resolved' })));
    expect(screen.getByRole('button', { name: 'Escalate ticket' })).toBeDisabled();
  });
});
