import { Badge } from '../../../components/Badge';
import type { SlaStatus } from '../../../types/api';
import { slaLabels } from '../slaPresentation';

interface SlaIndicatorProps {
  status?: SlaStatus | null;
}

export function SlaIndicator({ status }: SlaIndicatorProps) {
  const label = status == null ? undefined : slaLabels[status];
  return <Badge tone={label?.tone ?? 'neutral'}>{label?.text ?? 'SLA unavailable'}</Badge>;
}
