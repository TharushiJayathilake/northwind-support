import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { formatDateTime } from '../../lib/format';
import { escalatedTicket, ticketDetail } from '../../test/fixtures';
import { TicketDetailPage } from './TicketDetailPage';

vi.mock('../../hooks/useReferenceData', () => ({
  useReferenceData: () => ({ data: { agents: [], categories: [], customers: [] } }),
}));
const fetchMock = vi.fn<typeof fetch>();

async function mount() {
  await act(async () => {
    render(<MemoryRouter initialEntries={['/tickets/12']}><Routes>
      <Route path="/tickets/:id" element={<TicketDetailPage />} />
    </Routes></MemoryRouter>);
  });
}

async function submit() {
  fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Impact increased  ' } });
  fireEvent.change(screen.getByLabelText('Escalated by (temporary)'), { target: { value: '  Temporary actor  ' } });
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Escalate ticket' })); });
}

describe('Ticket escalation API and refresh', () => {
  beforeEach(() => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(ticketDetail())));
    vi.stubGlobal('fetch', fetchMock);
  });
  afterEach(() => { vi.resetAllMocks(); vi.unstubAllGlobals(); });

  it('POSTs the typed payload then refetches details and updates the existing page without reloading', async () => {
    let escalated = false;
    fetchMock.mockImplementation(async (input) => {
      if (String(input).endsWith('/escalate')) {
        escalated = true;
        return new Response(JSON.stringify(escalatedTicket()));
      }
      return new Response(JSON.stringify(escalated ? escalatedTicket().ticket : ticketDetail()));
    });
    await mount();
    const originalForm = screen.getByRole('form', { name: 'Escalate ticket' });
    const originalLocation = window.location.href;
    await submit();
    expect(fetchMock).toHaveBeenCalledTimes(3);
    const post = fetchMock.mock.calls[1];
    expect(post[0]).toBe('/api/tickets/12/escalate');
    expect(post[1]?.method).toBe('POST');
    expect(JSON.parse(String(post[1]?.body))).toEqual({ reason: 'Impact increased', escalatedBy: 'Temporary actor' });
    expect(fetchMock.mock.calls[2][0]).toBe('/api/tickets/12');
    expect(screen.getByText('Critical')).toBeInTheDocument();
    expect(screen.getByText('New owner')).toBeInTheDocument();
    expect(screen.getByText('Within SLA')).toBeInTheDocument();
    expect(screen.getByText(formatDateTime(escalatedTicket().ticket.dueAtUtc))).toBeInTheDocument();
    expect(screen.getByText('Ticket escalated successfully.')).toBeInTheDocument();
    expect(screen.getByRole('form', { name: 'Escalate ticket' })).toBe(originalForm);
    expect(window.location.href).toBe(originalLocation);
  });

  it.each([400, 404, 409])('HTTP %i leaves the existing ticket unchanged and does not refetch', async (status) => {
    fetchMock.mockImplementation(async (input) => String(input).endsWith('/escalate')
      ? new Response(JSON.stringify({ detail: `Escalation rejected (${status})` }), { status })
      : new Response(JSON.stringify(ticketDetail())));
    await mount();
    await submit();
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(screen.getByRole('alert')).toHaveTextContent(`Escalation rejected (${status})`);
    expect(screen.getByText('High')).toBeInTheDocument();
    expect(screen.getByText('Sara Lindqvist')).toBeInTheDocument();
    expect(screen.getByText('Breached')).toBeInTheDocument();
    expect(screen.getByText(formatDateTime(ticketDetail().dueAtUtc))).toBeInTheDocument();
    expect(screen.queryByText('Ticket escalated successfully.')).not.toBeInTheDocument();
  });

  it('preserves the confirmed response if the follow-up detail request fails', async () => {
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(ticketDetail())))
      .mockResolvedValueOnce(new Response(JSON.stringify(escalatedTicket())))
      .mockResolvedValueOnce(new Response(JSON.stringify({ detail: 'Refresh unavailable' }), { status: 500 }));
    await mount();
    await submit();
    expect(screen.getByText('Critical')).toBeInTheDocument();
    expect(screen.getByText('New owner')).toBeInTheDocument();
    expect(screen.getByText('Ticket escalated successfully.')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Refresh unavailable');
  });
});
