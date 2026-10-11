import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { escalatedTicket, ticketDetail } from '../../../test/fixtures';
import type { EscalateTicketResponse } from '../../../types/api';
import { EscalationForm } from './EscalationForm';

vi.mock('../../../api/tickets', () => ({ ticketsApi: { escalateTicket: vi.fn() } }));
const escalate = vi.mocked(ticketsApi.escalateTicket);
const onEscalated = vi.fn();
const onConflict = vi.fn();

async function submit(reason = 'Impact increased') {
  fireEvent.change(screen.getByLabelText('Reason'), { target: { value: reason } });
  await act(async () => { fireEvent.submit(screen.getByRole('form', { name: 'Escalate ticket' })); });
}

describe('EscalationForm', () => {
  beforeEach(() => {
    escalate.mockResolvedValue(escalatedTicket());
    render(<EscalationForm ticket={ticketDetail()} onEscalated={onEscalated} onConflict={onConflict} />);
  });
  afterEach(() => { vi.resetAllMocks(); });

  it('renders a reason textarea without a temporary actor input', () => {
    expect(screen.getByLabelText('Reason').tagName).toBe('TEXTAREA');
    expect(screen.queryByLabelText('Escalated by (temporary)')).not.toBeInTheDocument();
  });

  it.each(['', 'four', '   four   ', 'r'.repeat(501)])('blocks invalid reason length %s', async (reason) => {
    await submit(reason);
    expect(screen.getByText('Enter a reason of 5-500 characters.')).toBeInTheDocument();
    expect(escalate).not.toHaveBeenCalled();
    expect(onEscalated).not.toHaveBeenCalled();
  });

  it.each([5, 500])('accepts a reason of exactly %i characters', async (length) => {
    await submit('r'.repeat(length));
    expect(escalate).toHaveBeenCalledWith(12, { reason: 'r'.repeat(length) });
    expect(onEscalated).toHaveBeenCalledWith(escalatedTicket());
    expect(screen.getByRole('status')).toHaveTextContent('Ticket escalated successfully.');
  });

  it('trims a valid reason and sends no caller-supplied actor', async () => {
    await submit('  Valid reason  ');
    expect(escalate).toHaveBeenCalledWith(12, { reason: 'Valid reason' });
  });

  it('blocks repeat submissions and shows no success while pending', async () => {
    let resolvePending: (value: EscalateTicketResponse) => void = () => { throw new Error('No pending request'); };
    escalate.mockImplementationOnce(() => new Promise((resolve) => { resolvePending = resolve; }));
    await submit();
    expect(screen.getByRole('button', { name: 'Escalating...' })).toBeDisabled();
    expect(screen.getByLabelText('Reason')).toBeDisabled();
    expect(screen.queryByText('Ticket escalated successfully.')).not.toBeInTheDocument();
    await act(async () => { fireEvent.submit(screen.getByRole('form', { name: 'Escalate ticket' })); });
    expect(escalate).toHaveBeenCalledTimes(1);
    await act(async () => { resolvePending(escalatedTicket()); });
    expect(screen.getByRole('status')).toHaveTextContent('Ticket escalated successfully.');
  });

  it('shows HTTP 400 field validation feedback', async () => {
    escalate.mockRejectedValueOnce(new ApiError(400, 'Validation failed', { errors: {
      Reason: ['Reason was rejected'],
    } }));
    await submit();
    expect(screen.getByText('Reason was rejected')).toBeInTheDocument();
    expect(screen.getByText('Validation failed')).toBeInTheDocument();
    expect(screen.getByLabelText('Reason')).toHaveAttribute('aria-invalid', 'true');
    expect(onEscalated).not.toHaveBeenCalled();
  });

  it.each([[404, 'Ticket does not exist'], [409, 'Ticket is already Critical and cannot be escalated']])(
    'shows HTTP %i without changing ticket data', async (status, message) => {
      escalate.mockRejectedValueOnce(new ApiError(Number(status), String(message)));
      await submit();
      expect(screen.getByRole('alert')).toHaveTextContent(String(message));
      expect(onEscalated).not.toHaveBeenCalled();
      expect(onConflict).toHaveBeenCalledTimes(status === 409 ? 1 : 0);
      expect(screen.queryByText('Ticket escalated successfully.')).not.toBeInTheDocument();
      expect(screen.getByLabelText('Reason')).toHaveValue('Impact increased');
    });

  it('handles network failures without success or ticket changes', async () => {
    escalate.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    await submit();
    expect(screen.getByRole('alert')).toHaveTextContent('Could not escalate the ticket.');
    expect(onEscalated).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Escalate ticket' })).toBeEnabled();
  });
});
