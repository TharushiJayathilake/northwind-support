import { useCallback, useEffect, useRef, useState } from 'react';
import { toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { AsyncData } from '../../../hooks/useAsyncData';
import type { TicketDetail } from '../../../types/api';

/** Loads one ticket, and refetches whenever the id changes. */
export function useTicket(id: number): AsyncData<TicketDetail> {
  const [state, setState] = useState<{ ticketId: number; isLoading: boolean; data?: TicketDetail; error?: string }>({ ticketId: id, isLoading: true });
  const requests = useRef({ version: 0 });
  const currentId = useRef<number | undefined>(id);

  const load = useCallback(async () => {
    if (currentId.current !== id || !Number.isSafeInteger(id) || id <= 0) return;
    const version = ++requests.current.version;
    setState((current) => ({ ticketId: id, isLoading: true,
      data: current.ticketId === id ? current.data : undefined }));
    try {
      const data = await ticketsApi.getTicket(id);
      if (version === requests.current.version) setState({ ticketId: id, isLoading: false, data });
    } catch (error) {
      if (version === requests.current.version)
        setState((current) => ({ ...current, isLoading: false, error: toErrorMessage(error, 'Unable to load this ticket.') }));
    }
  }, [id]);

  useEffect(() => {
    const requestState = requests.current;
    currentId.current = id;
    void load();
    return () => { currentId.current = undefined; requestState.version++; };
  }, [id, load]);

  const setData = useCallback((data: TicketDetail) => {
    if (currentId.current !== id) return;
    requests.current.version++;
    setState({ ticketId: id, isLoading: false, data });
  }, [id]);

  return {
    data: state.ticketId === id ? state.data : undefined,
    error: state.ticketId === id ? state.error : undefined,
    isLoading: state.ticketId !== id || state.isLoading,
    reload: load, setData,
  };
}
