import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { page, ticketListItem } from '../../test/fixtures';
import type { SlaStatus } from '../../types/api';
import { TicketListPage } from './TicketListPage';

vi.mock('../../hooks/useReferenceData', () => ({
  useReferenceData: () => ({ data: {
    agents: [{ id: 4, fullName: 'Sara', email: 'sara@example.test', isActive: true, maxOpenTickets: 10, openTicketCount: 1, specializations: [] }],
    categories: [{ id: 2, name: 'Billing', isActive: true, requiresSpecialist: false, forcesCriticalPriority: false }],
    customers: [{ id: 2, name: 'Fabrikam', email: 'customer@example.test', phone: null, tier: 'Standard', openTicketCount: 1 }],
  } }),
}));

const fetchMock = vi.fn<typeof fetch>();
const statuses: SlaStatus[] = ['WithinSla', 'AtRisk', 'Breached', 'Met', 'NotApplicable'];
const requestUrl = () => new URL(String(fetchMock.mock.lastCall?.[0]), 'http://localhost');

async function mount() {
  await act(async () => { render(<MemoryRouter><TicketListPage /></MemoryRouter>); });
}

async function select(label: string, value: string) {
  await act(async () => { fireEvent.change(screen.getByLabelText(label), { target: { value } }); });
}

describe('SLA filtering UI', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    fetchMock.mockImplementation(async () => new Response(JSON.stringify(page([ticketListItem()]))));
    vi.stubGlobal('fetch', fetchMock);
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.resetAllMocks();
    vi.unstubAllGlobals();
  });

  it.each(statuses)('requests %s immediately without search interaction', async (status) => {
    await mount();
    expect(screen.getByRole('option', { name: 'All SLA statuses' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'At Risk' })).toBeInTheDocument();
    await select('SLA status', status);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(requestUrl().searchParams.get('slaStatus')).toBe(status);
    expect(requestUrl().searchParams.get('page')).toBe('1');
    expect(requestUrl().searchParams.has('search')).toBe(false);
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(fetchMock).toHaveBeenCalledTimes(2);
    // The returned API data is rendered as-is; the frontend does not apply another SLA filter.
    expect(screen.getByRole('link', { name: 'TCK-0012' })).toBeInTheDocument();
  });

  it('removes the SLA parameter when cleared and when all filters are reset', async () => {
    await mount();
    await select('SLA status', 'AtRisk');
    await select('SLA status', '');
    expect(requestUrl().searchParams.has('slaStatus')).toBe(false);
    await select('SLA status', 'Met');
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Clear filters' })); });
    expect(requestUrl().searchParams.has('slaStatus')).toBe(false);
    expect(screen.getByLabelText('SLA status')).toHaveValue('');
  });

  it('preserves all other filters and debounces search while SLA changes remain immediate', async () => {
    await mount();
    await select('Status', 'InProgress');
    await select('Priority', 'High');
    await select('Category', '2');
    await select('Customer', '2');
    await select('Assigned agent', '4');
    await act(async () => { fireEvent.change(screen.getByLabelText('Search'), { target: { value: 'inv' } }); });
    const before = fetchMock.mock.calls.length;
    await act(async () => { await vi.advanceTimersByTimeAsync(299); });
    expect(fetchMock).toHaveBeenCalledTimes(before);
    await act(async () => { fireEvent.change(screen.getByLabelText('Search'), { target: { value: 'invoice' } }); });
    await select('SLA status', 'AtRisk');
    expect(fetchMock).toHaveBeenCalledTimes(before + 1);
    const params = requestUrl().searchParams;
    expect(Object.fromEntries(params)).toMatchObject({ search: 'invoice', status: 'InProgress', priority: 'High',
      categoryId: '2', customerId: '2', assignedAgentId: '4', slaStatus: 'AtRisk' });
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(fetchMock).toHaveBeenCalledTimes(before + 1);
    await act(async () => { fireEvent.change(screen.getByLabelText('Search'), { target: { value: '' } }); });
    await act(async () => { await vi.advanceTimersByTimeAsync(299); });
    expect(fetchMock).toHaveBeenCalledTimes(before + 1);
    await act(async () => { await vi.advanceTimersByTimeAsync(1); });
    expect(requestUrl().searchParams.has('search')).toBe(false);
    expect(requestUrl().searchParams.get('slaStatus')).toBe('AtRisk');
    await act(async () => { fireEvent.click(screen.getByLabelText('Unassigned only')); });
    expect(requestUrl().searchParams.get('unassignedOnly')).toBe('true');
    expect(requestUrl().searchParams.get('slaStatus')).toBe('AtRisk');
  });

  it('resets pagination to page 1 when SLA changes', async () => {
    fetchMock.mockImplementation(async (input) => {
      const requested = new URL(String(input), 'http://localhost');
      return new Response(JSON.stringify({ ...page([ticketListItem()]), page: Number(requested.searchParams.get('page')), totalCount: 40, totalPages: 2 }));
    });
    await mount();
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Next' })); });
    expect(requestUrl().searchParams.get('page')).toBe('2');
    await select('SLA status', 'Breached');
    expect(requestUrl().searchParams.get('page')).toBe('1');
    expect(screen.getByText('Page 1 of 2')).toBeInTheDocument();
  });

  it('shows loading, an error, and an empty filtered result', async () => {
    let resolvePending: (response: Response) => void = () => { throw new Error('Request was not started'); };
    fetchMock.mockImplementationOnce(() => new Promise((resolve) => { resolvePending = resolve; }));
    await mount();
    expect(screen.getByRole('status')).toHaveTextContent('Loading tickets');
    await act(async () => { resolvePending(new Response(JSON.stringify(page([ticketListItem()])))); });
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ detail: 'SLA request failed' }), { status: 500 }));
    await select('SLA status', 'AtRisk');
    expect(screen.getByText('SLA request failed')).toBeInTheDocument();
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(page([]))));
    await select('SLA status', 'Met');
    expect(screen.getByText('No tickets match these filters')).toBeInTheDocument();
    expect(screen.queryByText('SLA request failed')).not.toBeInTheDocument();
  });
});
