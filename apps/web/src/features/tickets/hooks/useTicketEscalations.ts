import { useCallback, useEffect, useRef, useState } from 'react';
import { toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { TicketEscalation } from '../../../types/api';

interface HistoryState {
  ticketId: number;
  isLoading: boolean;
  data?: TicketEscalation[];
  error?: string;
}

export function useTicketEscalations(ticketId: number) {
  const [state, setState] = useState<HistoryState>({ ticketId, isLoading: true });
  const requests = useRef({ version: 0 });
  const currentId = useRef<number | undefined>(ticketId);

  const reload = useCallback(async () => {
    if (currentId.current !== ticketId || !Number.isSafeInteger(ticketId) || ticketId <= 0) return;
    const requestVersion = ++requests.current.version;
    setState((current) => ({ ticketId, isLoading: true,
      data: current.ticketId === ticketId ? current.data : undefined }));
    try {
      const data = await ticketsApi.getEscalations(ticketId);
      if (requestVersion === requests.current.version) setState({ ticketId, isLoading: false, data });
    } catch (error) {
      if (requestVersion === requests.current.version)
        setState((current) => ({ ...current, isLoading: false,
          error: toErrorMessage(error, 'Unable to load escalation history.') }));
    }
  }, [ticketId]);

  useEffect(() => {
    const requestState = requests.current;
    currentId.current = ticketId;
    void reload();
    return () => { currentId.current = undefined; requestState.version++; };
  }, [ticketId, reload]);

  // Hide the previous ticket's history immediately, before the new effect runs.
  return {
    data: state.ticketId === ticketId ? state.data : undefined,
    error: state.ticketId === ticketId ? state.error : undefined,
    isLoading: state.ticketId !== ticketId || state.isLoading,
    reload,
  };
}
