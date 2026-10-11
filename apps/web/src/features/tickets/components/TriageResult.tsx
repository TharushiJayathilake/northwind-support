import { formatUtcDateTime } from '../../../lib/format';
import type { TicketDetail, TicketTriage } from '../../../types/api';
import { PriorityBadge } from './TicketBadges';
import { SlaIndicator } from './SlaIndicator';

export type CreationResult = Pick<TicketDetail,
  'id' | 'reference' | 'priority' | 'dueAtUtc' | 'slaStatus' | 'triage'> & {
  assignedAgentName: string | null;
};

export function creationResult(ticket: TicketDetail): CreationResult {
  return {
    id: ticket.id, reference: ticket.reference, priority: ticket.priority,
    dueAtUtc: ticket.dueAtUtc, slaStatus: ticket.slaStatus, triage: ticket.triage,
    assignedAgentName: ticket.assignedAgent?.fullName ?? null,
  };
}

// Router state is external input, including when arriving through browser history.
export function readCreationResult(value: unknown, ticketId: number): CreationResult | undefined {
  if (typeof value !== 'object' || value === null || !('creationResult' in value)) return;
  const result = value.creationResult;
  if (typeof result !== 'object' || result === null) return;
  if (!('id' in result) || result.id !== ticketId ||
      !('reference' in result) || typeof result.reference !== 'string' ||
      !('priority' in result) || (result.priority !== 'Low' && result.priority !== 'Medium' &&
        result.priority !== 'High' && result.priority !== 'Critical') ||
      !('dueAtUtc' in result) || (result.dueAtUtc !== null &&
        (typeof result.dueAtUtc !== 'string' || !Number.isFinite(Date.parse(result.dueAtUtc)))) ||
      !('slaStatus' in result) || (result.slaStatus !== 'NotApplicable' && result.slaStatus !== 'WithinSla' &&
        result.slaStatus !== 'AtRisk' && result.slaStatus !== 'Breached' && result.slaStatus !== 'Met') ||
      !('assignedAgentName' in result) ||
        (result.assignedAgentName !== null && typeof result.assignedAgentName !== 'string')) return;
  const triage = 'triage' in result ? result.triage : undefined;
  let reasons: TicketTriage | undefined;
  if (triage != null) {
    if (typeof triage !== 'object' ||
      !('priorityReason' in triage) || typeof triage.priorityReason !== 'string' ||
      !('slaReason' in triage) || typeof triage.slaReason !== 'string' ||
      !('assignmentReason' in triage) || typeof triage.assignmentReason !== 'string') return;
    reasons = { priorityReason: triage.priorityReason, slaReason: triage.slaReason,
      assignmentReason: triage.assignmentReason };
  }
  return {
    id: ticketId, reference: result.reference, priority: result.priority,
    dueAtUtc: result.dueAtUtc, slaStatus: result.slaStatus,
    assignedAgentName: result.assignedAgentName,
    triage: reasons,
  };
}

export function TriageResult({ result }: { result: CreationResult }) {
  return (
    <section aria-label="Creation triage" className="triage-result">
      <h2>Creation triage</h2>
      <dl className="summary">
        <div><dt>Ticket reference</dt><dd>{result.reference}</dd></div>
        <div><dt>Applied priority</dt><dd><PriorityBadge priority={result.priority} /></dd></div>
        <div><dt>SLA deadline</dt><dd>{result.dueAtUtc ? formatUtcDateTime(result.dueAtUtc) : 'No SLA deadline'}</dd></div>
        <div><dt>Assigned agent</dt><dd>{result.assignedAgentName || 'Unassigned'}</dd></div>
        <div><dt>SLA status</dt><dd><SlaIndicator status={result.slaStatus} /></dd></div>
        <div><dt>Priority reason</dt><dd>{result.triage?.priorityReason || 'Priority explanation unavailable.'}</dd></div>
        <div><dt>SLA reason</dt><dd>{result.triage?.slaReason || 'SLA explanation unavailable.'}</dd></div>
        <div><dt>Assignment reason</dt><dd>{result.triage?.assignmentReason || 'Assignment explanation unavailable.'}</dd></div>
      </dl>
    </section>
  );
}
