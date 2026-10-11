import { act, fireEvent, render, screen, within } from '@testing-library/react';
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
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Escalate ticket' })); });
}

describe('Ticket escalation API and refresh', () => {
  beforeEach(() => {
    fetchMock.mockImplementation(async (input) => new Response(JSON.stringify(
      String(input).endsWith('/escalations') ? [] : ticketDetail())));
    vi.stubGlobal('fetch', fetchMock);
  });
  afterEach(() => { vi.resetAllMocks(); vi.unstubAllGlobals(); });

  it('POSTs the typed payload then refetches details and updates the existing page without reloading', async () => {
    let escalated = false;
    fetchMock.mockImplementation(async (input) => {
      if (String(input).endsWith('/escalations'))
        return new Response(JSON.stringify(escalated ? [escalatedTicket().escalation] : []));
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
    expect(fetchMock).toHaveBeenCalledTimes(5);
    const post = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/escalate'));
    if (!post) throw new Error('Expected escalation POST');
    expect(post[0]).toBe('/api/tickets/12/escalate');
    expect(post[1]?.method).toBe('POST');
    expect(JSON.parse(String(post[1]?.body))).toEqual({ reason: 'Impact increased' });
    expect(fetchMock.mock.calls.filter(([url]) => url === '/api/tickets/12')).toHaveLength(2);
    expect(fetchMock.mock.calls.filter(([url]) => url === '/api/tickets/12/escalations')).toHaveLength(2);
    expect(screen.getByRole('article', { name: 'Escalation #1' })).toHaveTextContent('Impact increased');
    expect(within(screen.getByRole('complementary')).getByText('Critical')).toBeInTheDocument();
    expect(screen.getByText('New owner')).toBeInTheDocument();
    expect(screen.getByText('Within SLA')).toBeInTheDocument();
    expect(screen.getByText(formatDateTime(escalatedTicket().ticket.dueAtUtc))).toBeInTheDocument();
    expect(screen.getByText('Ticket escalated successfully.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Escalate ticket' })).toBeDisabled();
    await act(async () => { fireEvent.submit(originalForm); });
    expect(fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/escalate'))).toHaveLength(1);
    expect(screen.getByRole('form', { name: 'Escalate ticket' })).toBe(originalForm);
    expect(window.location.href).toBe(originalLocation);
  });

  it.each([400, 404])('HTTP %i leaves the existing ticket unchanged and does not refetch', async (status) => {
    fetchMock.mockImplementation(async (input) => String(input).endsWith('/escalate')
      ? new Response(JSON.stringify({ detail: `Escalation rejected (${status})` }), { status })
      : new Response(JSON.stringify(String(input).endsWith('/escalations') ? [] : ticketDetail())));
    await mount();
    await submit();
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(fetchMock.mock.calls.filter(([url]) => url === '/api/tickets/12/escalations')).toHaveLength(1);
    expect(screen.queryByRole('article')).not.toBeInTheDocument();
    expect(screen.getByText('No escalations yet')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(`Escalation rejected (${status})`);
    expect(screen.getByText('High')).toBeInTheDocument();
    expect(screen.getByText('Sara Lindqvist')).toBeInTheDocument();
    expect(screen.getByText('Breached')).toBeInTheDocument();
    expect(screen.getByText(formatDateTime(ticketDetail().dueAtUtc))).toBeInTheDocument();
    expect(screen.queryByText('Ticket escalated successfully.')).not.toBeInTheDocument();
  });

  it('refreshes current ticket state on 409 without success or false history', async () => {
    let conflicted = false;
    fetchMock.mockImplementation(async (input) => {
      if (String(input).endsWith('/escalations')) return new Response('[]');
      if (String(input).endsWith('/escalate')) {
        conflicted = true;
        return new Response(JSON.stringify({ detail: 'Another user made this ticket Critical.' }), { status: 409 });
      }
      return new Response(JSON.stringify(conflicted ? ticketDetail({ priority: 'Critical' }) : ticketDetail()));
    });
    await mount(); await submit();
    expect(screen.getByRole('alert')).toHaveTextContent('Another user made this ticket Critical.');
    expect(screen.getByRole('button', { name: 'Escalate ticket' })).toBeDisabled();
    expect(within(screen.getByRole('complementary')).getByText('Critical')).toBeInTheDocument();
    expect(fetchMock.mock.calls.filter(([url]) => url === '/api/tickets/12')).toHaveLength(2);
    expect(fetchMock.mock.calls.filter(([url]) => url === '/api/tickets/12/escalations')).toHaveLength(1);
    expect(screen.queryByText('Ticket escalated successfully.')).not.toBeInTheDocument();
    expect(screen.queryByRole('article')).not.toBeInTheDocument();
  });

  it('preserves the confirmed response if the follow-up detail request fails', async () => {
    let escalated = false;
    fetchMock.mockImplementation(async (input) => {
      if (String(input).endsWith('/escalations'))
        return new Response(JSON.stringify(escalated ? [escalatedTicket().escalation] : []));
      if (String(input).endsWith('/escalate')) {
        escalated = true;
        return new Response(JSON.stringify(escalatedTicket()));
      }
      return escalated ? new Response(JSON.stringify({ detail: 'Refresh unavailable' }), { status: 500 })
        : new Response(JSON.stringify(ticketDetail()));
    });
    await mount();
    await submit();
    expect(within(screen.getByRole('complementary')).getByText('Critical')).toBeInTheDocument();
    expect(screen.getByText('New owner')).toBeInTheDocument();
    expect(screen.getByText('Ticket escalated successfully.')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Refresh unavailable');
  });

  it('refreshes ticket details even when history refresh fails', async () => {
    let escalated = false;
    fetchMock.mockImplementation(async (input) => {
      if (String(input).endsWith('/escalations'))
        return escalated ? new Response(JSON.stringify({ detail: 'History unavailable' }), { status: 500 })
          : new Response('[]');
      if (String(input).endsWith('/escalate')) {
        escalated = true;
        return new Response(JSON.stringify(escalatedTicket()));
      }
      return new Response(JSON.stringify(escalated ? escalatedTicket().ticket : ticketDetail()));
    });
    await mount();
    await submit();
    expect(within(screen.getByRole('complementary')).getByText('Critical')).toBeInTheDocument();
    expect(screen.getByText('Ticket escalated successfully.')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('History unavailable');
  });

  it('keeps refreshed history when the initial history request completes late', async () => {
    let historyCalls = 0;
    let escalated = false;
    let resolveOld: (response: Response) => void = () => { throw new Error('No initial history request'); };
    fetchMock.mockImplementation(async (input) => {
      if (String(input).endsWith('/escalations')) {
        if (++historyCalls === 1) return new Promise((resolve) => { resolveOld = resolve; });
        return new Response(JSON.stringify([escalatedTicket().escalation]));
      }
      if (String(input).endsWith('/escalate')) {
        escalated = true;
        return new Response(JSON.stringify(escalatedTicket()));
      }
      return new Response(JSON.stringify(escalated ? escalatedTicket().ticket : ticketDetail()));
    });
    await mount();
    expect(screen.getByRole('status')).toHaveTextContent('Loading escalation history');
    await submit();
    expect(screen.getByRole('article', { name: 'Escalation #1' })).toBeInTheDocument();
    await act(async () => { resolveOld(new Response('[]')); });
    expect(screen.getByRole('article', { name: 'Escalation #1' })).toBeInTheDocument();
    expect(screen.queryByText('No escalations yet')).not.toBeInTheDocument();
  });
});
