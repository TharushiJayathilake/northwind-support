import type { BadgeTone } from '../../components/Badge';
import type { SlaStatus } from '../../types/api';

export const slaStatuses: readonly SlaStatus[] = ['WithinSla', 'AtRisk', 'Breached', 'Met', 'NotApplicable'];

export const slaLabels: Record<SlaStatus, { text: string; tone: BadgeTone }> = {
  WithinSla: { text: 'Within SLA', tone: 'info' },
  AtRisk: { text: 'At Risk', tone: 'warning' },
  Breached: { text: 'Breached', tone: 'danger' },
  Met: { text: 'Met', tone: 'success' },
  NotApplicable: { text: 'No SLA', tone: 'neutral' },
};
