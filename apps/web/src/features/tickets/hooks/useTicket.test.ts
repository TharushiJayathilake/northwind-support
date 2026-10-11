import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../../api/tickets';
import { escalatedTicket, ticketDetail } from '../../../test/fixtures';
import type { TicketDetail } from '../../../types/api';
import { useTicket } from './useTicket';

vi.mock('../../../api/tickets', () => ({ ticketsApi: { getTicket: vi.fn() } }));
const getTicket = vi.mocked(ticketsApi.getTicket);
afterEach(() => { vi.resetAllMocks(); });

describe('useTicket refresh', () => {
  it('ignores a stale GET after an escalation result and a fresh GET', async () => {
    let resolveOld: (ticket: TicketDetail) => void = () => { throw new Error('No pending GET'); };
    getTicket.mockImplementationOnce(() => new Promise((resolve) => { resolveOld = resolve; }));
    const { result } = renderHook(() => useTicket(12));
    await act(async () => { result.current.setData(escalatedTicket().ticket); });
    getTicket.mockResolvedValueOnce(escalatedTicket().ticket);
    await act(async () => { await result.current.reload(); });
    await act(async () => { resolveOld(ticketDetail()); });
    expect(result.current.data).toEqual(escalatedTicket().ticket);
    expect(result.current.isLoading).toBe(false);
  });

  it('preserves the confirmed ticket and reports a refresh failure', async () => {
    getTicket.mockResolvedValueOnce(ticketDetail());
    const { result } = renderHook(() => useTicket(12));
    await act(async () => {});
    await act(async () => { result.current.setData(escalatedTicket().ticket); });
    getTicket.mockRejectedValueOnce(new Error('Offline'));
    await act(async () => { await result.current.reload(); });
    expect(result.current.data).toEqual(escalatedTicket().ticket);
    expect(result.current.error).toContain('Unable to load this ticket.');
  });

  it('does not let callbacks for a previous ticket replace the new ticket', async () => {
    getTicket.mockResolvedValue(ticketDetail());
    const { result, rerender } = renderHook(({ id }) => useTicket(id), { initialProps: { id: 12 } });
    await act(async () => {});
    const old = result.current;
    getTicket.mockResolvedValueOnce(ticketDetail({ id: 13 }));
    await act(async () => { rerender({ id: 13 }); });
    await act(async () => { old.setData(escalatedTicket().ticket); await old.reload(); });
    expect(result.current.data?.id).toBe(13);
  });
});
