import { useCallback, useEffect, useRef, useState } from 'react';
import { toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { AsyncData } from '../../../hooks/useAsyncData';
import type { TicketDetail } from '../../../types/api';

/** Loads one ticket, and refetches whenever the id changes. */
export function useTicket(id: number): AsyncData<TicketDetail> {
  const [state, setState] = useState<{ isLoading: boolean; data?: TicketDetail; error?: string }>({ isLoading: true });
  const requestVersion = useRef(0);
  const currentId = useRef(id);

  const load = useCallback(async () => {
    if (currentId.current !== id) return;
    const version = ++requestVersion.current;
    setState((current) => ({ ...current, isLoading: true, error: undefined }));
    try {
      const data = await ticketsApi.getTicket(id);
      if (version === requestVersion.current) setState({ isLoading: false, data });
    } catch (error) {
      if (version === requestVersion.current)
        setState((current) => ({ ...current, isLoading: false, error: toErrorMessage(error, 'Unable to load this ticket.') }));
    }
  }, [id]);

  useEffect(() => {
    currentId.current = id;
    void load();
    return () => { requestVersion.current++; };
  }, [id, load]);

  const setData = useCallback((data: TicketDetail) => {
    if (currentId.current !== id) return;
    requestVersion.current++;
    setState({ isLoading: false, data });
  }, [id]);

  return { ...state, reload: load, setData };
}
