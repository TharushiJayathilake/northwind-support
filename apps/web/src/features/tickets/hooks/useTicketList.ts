import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { PagedResult, TicketFilters, TicketListItem } from '../../../types/api';

const DEBOUNCE_MS = 300;

export const emptyFilters: TicketFilters = {
  search: '',
  status: undefined,
  priority: undefined,
  slaStatus: undefined,
  categoryId: undefined,
  customerId: undefined,
  assignedAgentId: undefined,
  unassignedOnly: false,
  page: 1,
  pageSize: 20,
  sortBy: 'createdAtUtc',
  sortDirection: 'desc',
};

interface State {
  isLoading: boolean;
  data?: PagedResult<TicketListItem>;
  error?: string;
}

/** Owns the ticket list's filters and the data they produce. */
export function useTicketList() {
  const [filters, setFilters] = useState<TicketFilters>(emptyFilters);
  const [state, setState] = useState<State>({ isLoading: true });
  const previousFilters = useRef(emptyFilters);
  const requestState = useRef({ version: 0 });

  const load = useCallback(async (requestFilters: TicketFilters) => {
    const version = ++requestState.current.version;
    setState((current) => ({ ...current, isLoading: true, error: undefined }));

    try {
      const data = await ticketsApi.getTickets(requestFilters);
      if (version === requestState.current.version) {
        setState({ isLoading: false, data, error: undefined });
      }
    } catch (error) {
      if (version === requestState.current.version) {
        const message = error instanceof ApiError ? error.message : 'Unable to load tickets.';
        setState((current) => ({ ...current, isLoading: false, error: message }));
      }
    }
  }, []);

  useEffect(() => {
    const requests = requestState.current;
    const previous = previousFilters.current;
    const searchChanged = filters.search !== previous.search;
    const otherFilterChanged = (Object.keys(filters) as (keyof TicketFilters)[]).some(
      (key) => key !== 'search'
        // A search edit also resets the page; it must still be debounced.
        && !(key === 'page' && searchChanged && filters.page === 1)
        && filters[key] !== previous[key],
    );
    previousFilters.current = filters;

    const handle = searchChanged && !otherFilterChanged
      ? window.setTimeout(() => void load(filters), DEBOUNCE_MS)
      : undefined;

    if (handle === undefined) {
      void load(filters);
    }

    return () => {
      window.clearTimeout(handle);
      // Also invalidate in-flight responses while search is pending or on unmount.
      requests.version++;
    };
  }, [filters, load]);

  const updateFilters = (patch: Partial<TicketFilters>) =>
    setFilters((current) => ({ ...current, ...patch, page: patch.page ?? 1 }));

  const resetFilters = () => setFilters(emptyFilters);

  return { filters, updateFilters, resetFilters, reload: () => load(filters), ...state };
}
