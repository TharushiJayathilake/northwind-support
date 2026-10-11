import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../../api/tickets';
import { page, ticketListItem } from '../../../test/fixtures';
import type { TicketFilters } from '../../../types/api';
import { useTicketList } from './useTicketList';

vi.mock('../../../api/tickets', () => ({
  ticketsApi: {
    getTickets: vi.fn(),
  },
}));

const getTickets = vi.mocked(ticketsApi.getTickets);

describe('useTicketList', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    getTickets.mockResolvedValue(page([ticketListItem()]));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
    vi.unstubAllGlobals();
  });

  it('searches with the text the user typed, once the typing settles', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(1);

    act(() => {
      result.current.updateFilters({ search: 'invoice' });
    });

    // Still inside the debounce window: no second request yet.
    expect(getTickets).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: 'invoice', page: 1 }),
    );

    expect(result.current.isLoading).toBe(false);
    expect(result.current.data?.items).toHaveLength(1);
  });

  it('requests InProgress immediately when only status changes on page 1', async () => {
    const { result } = renderHook(() => useTicketList());
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });

    await act(async () => result.current.updateFilters({ status: 'InProgress' }));

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(expect.objectContaining({
      status: 'InProgress', search: '', page: 1,
    }));
  });

  it('sends status in the HTTP request without interacting with search', async () => {
    const actual = await vi.importActual<typeof import('../../../api/tickets')>(
      '../../../api/tickets',
    );
    getTickets.mockImplementation(actual.ticketsApi.getTickets);
    const fetchMock = vi.fn<typeof fetch>().mockImplementation(async () =>
      new Response(JSON.stringify(page([ticketListItem()]))),
    );
    vi.stubGlobal('fetch', fetchMock);
    const { result } = renderHook(() => useTicketList());
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });

    await act(async () => result.current.updateFilters({ status: 'InProgress' }));

    expect(fetchMock).toHaveBeenCalledTimes(2);
    const requestUrl = new URL(String(fetchMock.mock.calls[1][0]), 'http://localhost');
    expect(requestUrl.pathname).toBe('/api/tickets');
    expect(requestUrl.searchParams.get('status')).toBe('InProgress');
    expect(requestUrl.searchParams.get('page')).toBe('1');
    expect(requestUrl.searchParams.has('search')).toBe(false);
  });

  const changes: [string, Partial<TicketFilters>][] = [
    ['priority', { priority: 'High' }],
    ['category', { categoryId: 2 }],
    ['customer', { customerId: 3 }],
    ['assigned agent', { assignedAgentId: 4 }],
    ['unassigned only', { unassignedOnly: true }],
    ['sort field', { sortBy: 'priority' }],
    ['sort direction', { sortDirection: 'asc' }],
    ['page size', { pageSize: 10 }],
    ['page', { page: 2 }],
  ];

  it.each(changes)('requests immediately when %s changes', async (_, patch) => {
    const { result } = renderHook(() => useTicketList());
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });

    await act(async () => result.current.updateFilters(patch));

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(expect.objectContaining(patch));
  });

  it('debounces repeated typing and requests only the latest text', async () => {
    const { result } = renderHook(() => useTicketList());
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });

    act(() => result.current.updateFilters({ search: 'inv' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(299); });
    act(() => result.current.updateFilters({ search: 'invoice' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(299); });
    expect(getTickets).toHaveBeenCalledTimes(1);

    await act(async () => { await vi.advanceTimersByTimeAsync(1); });
    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(expect.objectContaining({ search: 'invoice' }));
  });

  it('requests when search is cleared, preserving the selected status', async () => {
    const { result } = renderHook(() => useTicketList());
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    act(() => result.current.updateFilters({ status: 'InProgress', search: 'invoice' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });

    act(() => result.current.updateFilters({ search: '' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });

    expect(getTickets).toHaveBeenCalledTimes(3);
    expect(getTickets).toHaveBeenLastCalledWith(expect.objectContaining({
      search: '', status: 'InProgress', page: 1,
    }));
  });

  it('requests the latest combined filters immediately during pending search', async () => {
    const { result } = renderHook(() => useTicketList());
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    act(() => result.current.updateFilters({ search: 'invoice' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(100); });

    await act(async () => result.current.updateFilters({
      status: 'InProgress', priority: 'High', categoryId: 2,
    }));

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(expect.objectContaining({
      search: 'invoice', status: 'InProgress', priority: 'High', categoryId: 2, page: 1,
    }));
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(getTickets).toHaveBeenCalledTimes(2);
  });

  it('resets to page 1 immediately when a dropdown changes', async () => {
    const { result } = renderHook(() => useTicketList());
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    act(() => result.current.updateFilters({ page: 2 }));
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });

    await act(async () => result.current.updateFilters({ status: 'Resolved' }));

    expect(getTickets).toHaveBeenCalledTimes(3);
    expect(getTickets).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'Resolved', page: 1 }));
    expect(result.current.filters.page).toBe(1);
  });

  it('keeps search debounced when typing resets the page', async () => {
    const { result } = renderHook(() => useTicketList());
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    act(() => result.current.updateFilters({ page: 2 }));
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });

    act(() => result.current.updateFilters({ search: 'invoice' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(299); });
    expect(getTickets).toHaveBeenCalledTimes(2);
    await act(async () => { await vi.advanceTimersByTimeAsync(1); });
    expect(getTickets).toHaveBeenLastCalledWith(expect.objectContaining({ search: 'invoice', page: 1 }));
  });

  it('clears active filters and requests immediately', async () => {
    const { result } = renderHook(() => useTicketList());
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    act(() => result.current.updateFilters({ status: 'Resolved', search: 'invoice', page: 2 }));
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });

    await act(async () => result.current.resetFilters());

    expect(getTickets).toHaveBeenCalledTimes(3);
    expect(getTickets).toHaveBeenLastCalledWith(expect.objectContaining({
      status: undefined, search: '', page: 1,
    }));
  });

  it('does not let an older response overwrite newer results', async () => {
    const { result } = renderHook(() => useTicketList());
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    let resolveOld!: (value: ReturnType<typeof page>) => void;
    getTickets.mockReturnValueOnce(new Promise((resolve) => { resolveOld = resolve; }));
    act(() => result.current.updateFilters({ status: 'Open' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    getTickets.mockResolvedValueOnce(page([ticketListItem({ title: 'Newer results' })]));
    act(() => result.current.updateFilters({ status: 'Resolved' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(result.current.data?.items[0].title).toBe('Newer results');

    await act(async () => resolveOld(page([ticketListItem({ title: 'Older results' })])));
    expect(result.current.data?.items[0].title).toBe('Newer results');
    expect(result.current.isLoading).toBe(false);
  });

  it('does not show an error from an obsolete request', async () => {
    const { result } = renderHook(() => useTicketList());
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    let rejectOld!: (error: Error) => void;
    getTickets.mockReturnValueOnce(new Promise((_, reject) => { rejectOld = reject; }));
    act(() => result.current.updateFilters({ status: 'Open' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    act(() => result.current.updateFilters({ status: 'Resolved' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });

    await act(async () => rejectOld(new Error('Obsolete request failed')));
    expect(result.current.error).toBeUndefined();
    expect(result.current.isLoading).toBe(false);
  });

  it('invalidates old responses as soon as a new search is pending', async () => {
    const { result } = renderHook(() => useTicketList());
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    let resolveOld!: (value: ReturnType<typeof page>) => void;
    getTickets.mockReturnValueOnce(new Promise((resolve) => { resolveOld = resolve; }));
    await act(async () => result.current.updateFilters({ status: 'Open' }));

    act(() => result.current.updateFilters({ search: 'invoice' }));
    await act(async () => resolveOld(page([ticketListItem({ title: 'Obsolete results' })])));
    expect(result.current.data?.items[0].title).not.toBe('Obsolete results');

    getTickets.mockResolvedValueOnce(page([ticketListItem({ title: 'Search results' })]));
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(result.current.data?.items[0].title).toBe('Search results');
    expect(result.current.isLoading).toBe(false);
  });
});
