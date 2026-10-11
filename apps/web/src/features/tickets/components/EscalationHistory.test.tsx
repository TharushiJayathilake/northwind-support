import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { formatUtcDateTime } from '../../../lib/format';
import { escalatedTicket } from '../../../test/fixtures';
import { EscalationHistory } from './EscalationHistory';

describe('EscalationHistory', () => {
  it('renders ordered audit records with before/after values and explicit UTC timestamps', () => {
    const oldest = escalatedTicket().escalation;
    const newest = { ...oldest, id: 2, fromPriority: oldest.toPriority, escalatedAtUtc: '2026-09-27T12:00:00Z', reason: 'New escalation reason' };
    render(<EscalationHistory history={[newest, oldest]} isLoading={false} onRetry={vi.fn()}
      agents={[{ id: 4, fullName: 'Previous owner' }, { id: 9, fullName: 'Next owner' }]} />);
    const records = screen.getAllByRole('article');
    expect(records.map((record) => record.getAttribute('aria-label'))).toEqual(['Escalation #2', 'Escalation #1']);
    const old = within(records[1]);
    expect(old.getByText('High')).toBeInTheDocument();
    expect(old.getByText('Critical')).toBeInTheDocument();
    expect(old.getByText('Previous owner')).toBeInTheDocument();
    expect(old.getByText('Next owner')).toBeInTheDocument();
    if (oldest.fromDueAtUtc === null || oldest.toDueAtUtc === null) throw new Error('Expected audit deadlines');
    expect(old.getByText(formatUtcDateTime(oldest.fromDueAtUtc))).toHaveAttribute('datetime', oldest.fromDueAtUtc);
    expect(old.getByText(formatUtcDateTime(oldest.toDueAtUtc))).toHaveAttribute('datetime', oldest.toDueAtUtc);
    expect(old.getByText(formatUtcDateTime(oldest.escalatedAtUtc))).toHaveAttribute('datetime', oldest.escalatedAtUtc);
    expect(old.getByText('Impact increased')).toBeInTheDocument();
    expect(old.getByText('Temporary actor')).toBeInTheDocument();
    expect(within(records[0]).getByText('New escalation reason')).toBeInTheDocument();
  });

  it('preserves the backend order even when timestamps match', () => {
    const record = escalatedTicket().escalation;
    render(<EscalationHistory history={[{ ...record, id: 9 }, record]} isLoading={false} onRetry={vi.fn()} />);
    expect(screen.getAllByRole('article').map((item) => item.getAttribute('aria-label'))).toEqual(['Escalation #9', 'Escalation #1']);
  });

  it('uses readable nullable fallbacks and unknown agent IDs', () => {
    const record = { ...escalatedTicket().escalation, fromAgentId: null, toAgentId: 99, fromDueAtUtc: null, toDueAtUtc: null };
    render(<EscalationHistory history={[record]} isLoading={false} onRetry={vi.fn()} />);
    expect(screen.getByText('Unassigned')).toBeInTheDocument();
    expect(screen.getByText('Agent #99')).toBeInTheDocument();
    expect(screen.getAllByText('No SLA deadline')).toHaveLength(2);
  });

  it('shows an empty state', () => {
    render(<EscalationHistory history={[]} isLoading={false} onRetry={vi.fn()} />);
    expect(screen.getByText('No escalations yet')).toBeInTheDocument();
    expect(screen.queryByRole('article')).not.toBeInTheDocument();
  });

  it('shows loading without prematurely showing an empty state', () => {
    render(<EscalationHistory isLoading onRetry={vi.fn()} />);
    expect(screen.getByRole('status')).toHaveTextContent('Loading escalation history');
    expect(screen.getByRole('region', { name: 'Escalation history' })).toHaveAttribute('aria-busy', 'true');
    expect(screen.queryByText('No escalations yet')).not.toBeInTheDocument();
  });

  it('shows an error and allows retry', () => {
    const retry = vi.fn();
    render(<EscalationHistory isLoading={false} error="History unavailable" onRetry={retry} />);
    expect(screen.getByRole('alert')).toHaveTextContent('History unavailable');
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(retry).toHaveBeenCalledOnce();
  });
});
