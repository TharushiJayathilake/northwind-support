import { Link, useLocation, useParams } from 'react-router-dom';
import { ErrorMessage } from '../../components/ErrorMessage';
import { Spinner } from '../../components/Spinner';
import { useReferenceData } from '../../hooks/useReferenceData';
import { AssignAgentControl } from './components/AssignAgentControl';
import { EscalationForm } from './components/EscalationForm';
import { EscalationHistory } from './components/EscalationHistory';
import { StatusActions } from './components/StatusActions';
import { TicketSummary } from './components/TicketSummary';
import { readCreationResult, TriageResult } from './components/TriageResult';
import { useTicket } from './hooks/useTicket';
import { useTicketEscalations } from './hooks/useTicketEscalations';

export function TicketDetailPage() {
  const { id } = useParams<{ id: string }>();
  const ticketId = Number(id);
  const location = useLocation();
  const navigationState: unknown = location.state;
  const created = readCreationResult(navigationState, ticketId);

  const { data: ticket, isLoading, error, reload, setData } = useTicket(ticketId);
  const reference = useReferenceData();
  const history = useTicketEscalations(ticketId);

  if (!Number.isSafeInteger(ticketId) || ticketId <= 0) {
    return <ErrorMessage message="That is not a valid ticket reference." />;
  }

  if (isLoading && !ticket) {
    return <Spinner label="Loading ticket" />;
  }

  if (error && !ticket) {
    return <ErrorMessage message={error} onRetry={() => void reload()} />;
  }

  if (!ticket) {
    return null;
  }

  return (
    <section>
      <header className="page-header">
        <div>
          <p className="breadcrumb">
            <Link to="/tickets">Tickets</Link> / {ticket.reference}
          </p>
          <h1>{ticket.title}</h1>
        </div>
      </header>

      {error && <ErrorMessage message={error} onRetry={() => void reload()} />}
      {created && <TriageResult result={created} />}

      <div className="detail-layout">
        <div className="detail-layout__main">
          <div className="card">
            <h2>Description</h2>
            <p className="description">{ticket.description}</p>
          </div>

          <StatusActions ticket={ticket} onUpdated={setData} />
          <EscalationForm ticket={ticket} onConflict={reload} onEscalated={async (response) => {
            setData(response.ticket);
            await Promise.all([reload(), history.reload()]);
          }} />
          <EscalationHistory history={history.data} isLoading={history.isLoading} error={history.error}
            agents={reference.data?.agents ?? []} onRetry={() => void history.reload()} />
        </div>

        <aside className="detail-layout__side">
          <TicketSummary ticket={ticket} />

          <AssignAgentControl
            ticket={ticket}
            agents={reference.data?.agents ?? []}
            onUpdated={setData}
          />
        </aside>
      </div>
    </section>
  );
}
