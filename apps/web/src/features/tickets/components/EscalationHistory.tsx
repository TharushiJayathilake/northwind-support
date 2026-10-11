import { EmptyState } from '../../../components/EmptyState';
import { ErrorMessage } from '../../../components/ErrorMessage';
import { Spinner } from '../../../components/Spinner';
import { formatUtcDateTime } from '../../../lib/format';
import type { AgentSummary, TicketEscalation } from '../../../types/api';
import { PriorityBadge } from './TicketBadges';

interface EscalationHistoryProps {
  history?: readonly TicketEscalation[];
  agents?: readonly AgentSummary[];
  isLoading: boolean;
  error?: string;
  onRetry: () => void;
}

export function EscalationHistory({ history, agents = [], isLoading, error, onRetry }: EscalationHistoryProps) {
  const agentName = (id: number | null) => id === null ? 'Unassigned'
    : agents.find((agent) => agent.id === id)?.fullName ?? `Agent #${id}`;
  const deadline = (value: string | null) => value
    ? <time dateTime={value}>{formatUtcDateTime(value)}</time> : 'No SLA deadline';

  return (
    <section aria-label="Escalation history" aria-busy={isLoading}>
      <h2>Escalation history</h2>
      {isLoading && <Spinner label="Loading escalation history" />}
      {error && <ErrorMessage message={error} onRetry={onRetry} />}
      {!isLoading && !error && history?.length === 0 && <EmptyState title="No escalations yet" />}
      {history && history.length > 0 && (
        <ol className="escalation-history">
          {history.map((record) => (
            <li key={record.id} className="escalation-history__entry">
              <article aria-label={`Escalation #${record.id}`}>
                <h3>Escalation #{record.id}</h3>
                <dl className="summary">
                  <div><dt>Previous priority</dt><dd><PriorityBadge priority={record.fromPriority} /></dd></div>
                  <div><dt>New priority</dt><dd><PriorityBadge priority={record.toPriority} /></dd></div>
                  <div><dt>Previous assigned agent</dt><dd>{agentName(record.fromAgentId)}</dd></div>
                  <div><dt>New assigned agent</dt><dd>{agentName(record.toAgentId)}</dd></div>
                  <div><dt>Previous SLA deadline</dt><dd>{deadline(record.fromDueAtUtc)}</dd></div>
                  <div><dt>New SLA deadline</dt><dd>{deadline(record.toDueAtUtc)}</dd></div>
                  <div><dt>Escalated by</dt><dd>{record.escalatedBy}</dd></div>
                  <div><dt>Escalated at</dt><dd><time dateTime={record.escalatedAtUtc}>
                    {formatUtcDateTime(record.escalatedAtUtc)}
                  </time></dd></div>
                </dl>
                <p className="description">{record.reason}</p>
              </article>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
