import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { formatUtcDateTime } from '../../lib/format';
import { ticketDetail } from '../../test/fixtures';
import type { TicketDetail } from '../../types/api';
import { CreateTicketPage } from './CreateTicketPage';
import { TicketDetailPage } from './TicketDetailPage';
import { TriageResult } from './components/TriageResult';
import { creationResult, readCreationResult } from './creationResult';

vi.mock('../../hooks/useReferenceData', () => ({
  useReferenceData: () => ({ data: {
    agents: [],
    customers: [{ id: 2, name: 'Fabrikam Inc', tier: 'Standard' }],
    categories: [{ id: 2, name: 'Billing', isActive: true }],
  } }),
}));
const fetchMock = vi.fn<typeof fetch>();
const created = (): TicketDetail => ticketDetail({
  priority: 'Critical', slaStatus: 'WithinSla',
  triage: {
    priorityReason: 'Category requires Critical priority.',
    slaReason: 'Standard customer SLA applied.',
    assignmentReason: 'Selected eligible agent with the lowest workload.',
  },
});

async function mount(path = '/tickets/new', state?: unknown) {
  await act(async () => {
    render(<MemoryRouter initialEntries={[{ pathname: path, state }]}><Routes>
      <Route path="/tickets/new" element={<CreateTicketPage />} />
      <Route path="/tickets/:id" element={<TicketDetailPage />} />
    </Routes></MemoryRouter>);
  });
}

async function submit() {
  fireEvent.change(screen.getByLabelText('Title'), { target: { value: '  Billing issue  ' } });
  fireEvent.change(screen.getByLabelText('Description'), { target: { value: '  Incorrect invoice total  ' } });
  fireEvent.change(screen.getByLabelText('Customer'), { target: { value: '2' } });
  fireEvent.change(screen.getByLabelText('Category'), { target: { value: '2' } });
  fireEvent.change(screen.getByLabelText('Requested priority'), { target: { value: 'Low' } });
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Raise ticket' })); });
}

describe('Ticket creation triage results', () => {
  beforeEach(() => {
    fetchMock.mockImplementation(async (input, init) => new Response(JSON.stringify(
      init?.method === 'POST' ? created() : String(input).endsWith('/escalations') ? [] : ticketDetail({ triage: null })),
    { status: init?.method === 'POST' ? 201 : 200 }));
    vi.stubGlobal('fetch', fetchMock);
  });
  afterEach(() => { vi.resetAllMocks(); vi.unstubAllGlobals(); });

  it('shows server-applied priority, reference, UTC deadline, agent, SLA badge and all reasons after creation', async () => {
    await mount();
    await submit();
    const result = within(screen.getByRole('region', { name: 'Creation triage' }));
    for (const text of ['TCK-0012', 'Critical', 'Sara Lindqvist', 'Within SLA',
      formatUtcDateTime(created().dueAtUtc ?? ''), ...Object.values(created().triage ?? {})]) {
      expect(result.getByText(text)).toBeInTheDocument();
    }
    expect(result.getByText('Within SLA')).toHaveClass('badge');
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/tickets');
    expect(JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body))).toEqual({
      title: 'Billing issue', description: 'Incorrect invoice total', customerId: 2,
      categoryId: 2, requestedPriority: 'Low',
    });
  });

  it('preserves the POST-only snapshot on opening details even though GET has no reasons and different current values', async () => {
    await mount(); await submit();
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Open TCK-0012' })); });
    const result = within(screen.getByRole('region', { name: 'Creation triage' }));
    expect(result.getByText('Critical')).toBeInTheDocument();
    expect(result.getByText('Category requires Critical priority.')).toBeInTheDocument();
    expect(within(screen.getByRole('complementary')).getByText('High')).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([url]) => url === '/api/tickets/12')).toBe(true);
  });

  it('shows Unassigned with the server explanation when no eligible agent exists', async () => {
    const ticket = created();
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ ...ticket, assignedAgent: null,
      triage: { ...ticket.triage, assignmentReason: 'No eligible agents are available.' } }), { status: 201 }));
    await mount(); await submit();
    expect(screen.getByText('Unassigned')).toBeInTheDocument();
    expect(screen.getByText('No eligible agents are available.')).toBeInTheDocument();
  });

  it.each([undefined, null])('handles missing triage (%s) and nullable deadline and agent', (triage) => {
    render(<TriageResult result={creationResult(ticketDetail({ triage, dueAtUtc: null,
      assignedAgent: null, slaStatus: 'NotApplicable' }))} />);
    expect(screen.getByText('No SLA deadline')).toBeInTheDocument();
    expect(screen.getByText('No SLA')).toBeInTheDocument();
    expect(screen.getByText('Unassigned')).toBeInTheDocument();
    expect(screen.getByText('Priority explanation unavailable.')).toBeInTheDocument();
    expect(screen.getByText('SLA explanation unavailable.')).toBeInTheDocument();
    expect(screen.getByText('Assignment explanation unavailable.')).toBeInTheDocument();
  });

  it.each([400, 500])('HTTP %i does not display a successful result', async (status) => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ detail: 'Creation rejected.' }), { status }));
    await mount(); await submit();
    expect(screen.getByRole('alert')).toHaveTextContent('Creation rejected.');
    expect(screen.queryByRole('region', { name: 'Creation triage' })).not.toBeInTheDocument();
    expect(screen.getByLabelText('Title')).toHaveValue('  Billing issue  ');
  });

  it('network failure leaves the form visible without success', async () => {
    fetchMock.mockRejectedValue(new Error('Network unavailable'));
    await mount(); await submit();
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Creation triage' })).not.toBeInTheDocument();
  });

  it('Raise another clears the result and preserves the creation workflow', async () => {
    await mount(); await submit();
    fireEvent.click(screen.getByRole('button', { name: 'Raise another' }));
    expect(screen.queryByRole('region', { name: 'Creation triage' })).not.toBeInTheDocument();
    expect(screen.getByLabelText('Title')).toHaveValue('');
  });

  it.each([undefined, { creationResult: { id: 12 } },
    { creationResult: creationResult(ticketDetail({ id: 99 })) }])(
    'direct visits or absent/invalid/unrelated navigation state safely show normal details (%j)', async (state) => {
      await mount('/tickets/12', state);
      expect(screen.getByRole('heading', { name: ticketDetail().title })).toBeInTheDocument();
      expect(screen.queryByRole('region', { name: 'Creation triage' })).not.toBeInTheDocument();
    });

  it('accepts valid nullable snapshots and rejects malformed reasons or dates', () => {
    const result = creationResult(ticketDetail({ assignedAgent: null, dueAtUtc: null }));
    expect(readCreationResult({ creationResult: result }, 12)).toEqual(result);
    expect(readCreationResult({ creationResult: { ...result, dueAtUtc: 'bad date' } }, 12)).toBeUndefined();
    expect(readCreationResult({ creationResult: { ...result, triage: { priorityReason: 5 } } }, 12)).toBeUndefined();
    expect(readCreationResult(null, 12)).toBeUndefined();
  });
});
