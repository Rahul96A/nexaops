import { Chip } from '@mui/material';
import type { ProblemStatus, RootCauseConfidence } from '@/api/types';

type ChipColour = 'default' | 'primary' | 'secondary' | 'error' | 'info' | 'success' | 'warning';

const statusLabels: Record<ProblemStatus, string> = {
  New: 'New',
  Investigating: 'Investigating',
  KnownError: 'Known error',
  FixInProgress: 'Fix in progress',
  Resolved: 'Resolved',
  Closed: 'Closed',
  Cancelled: 'Cancelled',
};

const statusColours: Record<ProblemStatus, ChipColour> = {
  New: 'default',
  Investigating: 'primary',

  // Success, not warning: a published known error means the service desk has something that
  // works. It is a good state to be in, even though the cause is still there.
  KnownError: 'success',

  FixInProgress: 'info',
  Resolved: 'success',
  Closed: 'default',
  Cancelled: 'default',
};

export function ProblemStatusChip({
  status,
  size = 'small',
}: {
  status: ProblemStatus;
  size?: 'small' | 'medium';
}) {
  return (
    <Chip
      label={statusLabels[status] ?? status}
      color={statusColours[status] ?? 'default'}
      size={size}
      variant={status === 'Closed' || status === 'Cancelled' ? 'outlined' : 'filled'}
    />
  );
}

const confidenceLabels: Record<RootCauseConfidence, string> = {
  Suspected: 'Suspected',
  Probable: 'Probable',
  Confirmed: 'Confirmed',
};

const confidenceColours: Record<RootCauseConfidence, ChipColour> = {
  Suspected: 'default',
  Probable: 'warning',
  Confirmed: 'success',
};

/**
 * Confidence is shown next to the root cause because "we think it is the database" and "we
 * proved it is the database" justify very different amounts of spend on the permanent fix.
 */
export function ConfidenceChip({ confidence }: { confidence: RootCauseConfidence }) {
  return (
    <Chip
      label={confidenceLabels[confidence] ?? confidence}
      color={confidenceColours[confidence] ?? 'default'}
      size="small"
      variant="outlined"
    />
  );
}
