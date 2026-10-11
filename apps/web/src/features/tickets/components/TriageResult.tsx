import { formatUtcDateTime } from '../../../lib/format';
import type { CreationResult } from '../creationResult';
import { PriorityBadge } from './TicketBadges';
import { SlaIndicator } from './SlaIndicator';

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
