import type { TicketDetail, TicketTriage } from '../../types/api';

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
