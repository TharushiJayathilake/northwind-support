import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { ticketListItem } from '../../../test/fixtures';
import type { SlaStatus, TicketDetail } from '../../../types/api';
import { SlaIndicator } from './SlaIndicator';
import { TicketSummary } from './TicketSummary';
import { TicketTable } from './TicketTable';
import { TicketDetailPage } from '../TicketDetailPage';
import { useTicket } from '../hooks/useTicket';

vi.mock('../hooks/useTicket', () => ({ useTicket: vi.fn() }));
vi.mock('../../../hooks/useReferenceData', () => ({
  useReferenceData: () => ({ data: { agents: [], categories: [], customers: [] } }),
}));

const cases: [SlaStatus, string, string][] = [
  ['WithinSla', 'Within SLA', 'info'], ['AtRisk', 'At Risk', 'warning'],
  ['Breached', 'Breached', 'danger'], ['Met', 'Met', 'success'], ['NotApplicable', 'No SLA', 'neutral'],
];

describe('SlaIndicator', () => {
  it.each(cases)('renders %s with readable text and an existing badge tone', (status, label, tone) => {
    render(<SlaIndicator status={status} />);
    expect(screen.getByText(label)).toHaveClass(`badge--${tone}`);
  });

  it.each([null, undefined])('handles missing status %s without claiming there is no SLA', (status) => {
    render(<SlaIndicator status={status} />);
    expect(screen.getByText('SLA unavailable')).toHaveClass('badge--neutral');
  });

  it.each(cases)('displays %s in the ticket list and detail summary', (status, label, tone) => {
    const row = ticketListItem({ slaStatus: status });
    const list = render(<MemoryRouter><TicketTable tickets={[row]} /></MemoryRouter>);
    expect(screen.getByText(label)).toHaveClass(`badge--${tone}`);
    list.unmount();
    const detail: TicketDetail = {
      ...row, description: 'Support request',
      customer: { ...row.customer, email: 'customer@example.test', phone: null },
      category: { ...row.category, requiresSpecialist: false },
    };
    render(<TicketSummary ticket={detail} />);
    expect(screen.getByText(label)).toHaveClass(`badge--${tone}`);
  });

  it.each(cases)('displays %s on the ticket detail page', (status, label, tone) => {
    const row = ticketListItem({ slaStatus: status });
    const detail: TicketDetail = {
      ...row, description: 'Support request',
      customer: { ...row.customer, email: 'customer@example.test', phone: null },
      category: { ...row.category, requiresSpecialist: false },
    };
    vi.mocked(useTicket).mockReturnValue({ data: detail, isLoading: false, reload: async () => {}, setData: vi.fn() });
    render(<MemoryRouter initialEntries={['/tickets/12']}><Routes>
      <Route path="/tickets/:id" element={<TicketDetailPage />} />
    </Routes></MemoryRouter>);
    expect(screen.getByText(label)).toHaveClass(`badge--${tone}`);
  });
});
