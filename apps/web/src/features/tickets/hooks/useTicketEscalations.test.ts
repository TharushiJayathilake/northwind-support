import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { escalatedTicket } from '../../../test/fixtures';
import type { TicketEscalation } from '../../../types/api';
import { useTicketEscalations } from './useTicketEscalations';

vi.mock('../../../api/tickets', () => ({ ticketsApi: { getEscalations: vi.fn() } }));
const getHistory = vi.mocked(ticketsApi.getEscalations);
const record = escalatedTicket().escalation;
afterEach(() => { vi.resetAllMocks(); });

function deferred() {
  let resolve: (data: TicketEscalation[]) => void = () => { throw new Error('Not initialized'); };
  let reject: (error: Error) => void = () => { throw new Error('Not initialized'); };
  const promise = new Promise<TicketEscalation[]>((accept, fail) => { resolve = accept; reject = fail; });
  return { promise, resolve, reject };
}

describe('useTicketEscalations', () => {
  it('loads the requested ticket and preserves response order', async () => {
    getHistory.mockResolvedValue([record, { ...record, id: 2 }]);
    const { result } = renderHook(() => useTicketEscalations(12));
    await act(async () => {});
    expect(getHistory).toHaveBeenCalledWith(12);
    expect(result.current.data?.map((item) => item.id)).toEqual([1, 2]);
    expect(result.current.isLoading).toBe(false);
  });

  it('hides previous history on navigation and ignores the old ticket response', async () => {
    getHistory.mockResolvedValueOnce([record]);
    const { result, rerender } = renderHook(({ id }) => useTicketEscalations(id), { initialProps: { id: 12 } });
    await act(async () => {});
    const old = deferred();
    getHistory.mockReturnValueOnce(old.promise);
    act(() => { void result.current.reload(); });
    const next = deferred();
    getHistory.mockReturnValueOnce(next.promise);
    await act(async () => { rerender({ id: 13 }); });
    expect(result.current.data).toBeUndefined();
    expect(result.current.isLoading).toBe(true);
    await act(async () => { next.resolve([{ ...record, ticketId: 13, id: 3 }]); });
    await act(async () => { old.resolve([record]); });
    expect(result.current.data?.[0].ticketId).toBe(13);
  });

  it.each([false, true])('ignores obsolete refresh responses (error=%s)', async (error) => {
    const old = deferred();
    getHistory.mockReturnValueOnce(old.promise);
    const { result } = renderHook(() => useTicketEscalations(12));
    getHistory.mockResolvedValueOnce([record]);
    await act(async () => { await result.current.reload(); });
    await act(async () => { if (error) old.reject(new Error('Obsolete failure')); else old.resolve([]); });
    expect(result.current.data).toEqual([record]);
    expect(result.current.error).toBeUndefined();
  });

  it('reports API errors and retries successfully', async () => {
    getHistory.mockRejectedValueOnce(new ApiError(404, 'Ticket does not exist'));
    const { result } = renderHook(() => useTicketEscalations(12));
    await act(async () => {});
    expect(result.current.error).toBe('Ticket does not exist');
    getHistory.mockResolvedValueOnce([]);
    await act(async () => { await result.current.reload(); });
    expect(result.current.data).toEqual([]);
    expect(result.current.error).toBeUndefined();
  });

  it('ignores completion after unmount and does not start another request', async () => {
    const pending = deferred();
    getHistory.mockReturnValueOnce(pending.promise);
    const { result, unmount } = renderHook(() => useTicketEscalations(12));
    const reload = result.current.reload;
    unmount();
    await act(async () => { pending.resolve([record]); await reload(); });
    expect(getHistory).toHaveBeenCalledTimes(1);
  });

  it('does not fetch invalid ticket IDs', async () => {
    renderHook(() => useTicketEscalations(Number.NaN));
    await act(async () => {});
    expect(getHistory).not.toHaveBeenCalled();
  });
});
