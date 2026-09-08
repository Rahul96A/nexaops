import { Chip, Tooltip } from '@mui/material';
import type { ChangeOutcome, ChangeRisk, ChangeStatus, ChangeType } from '@/api/types';

type ChipColour = 'default' | 'primary' | 'secondary' | 'error' | 'info' | 'success' | 'warning';

const typeLabels: Record<ChangeType, string> = {
  Standard: 'Standard',
  Normal: 'Normal',
  Emergency: 'Emergency',
};

const typeHelp: Record<ChangeType, string> = {
  Standard: 'Pre-authorised procedure. No approval needed each time.',
  Normal: 'Assessed and approved by the change advisory board before it is scheduled.',
  Emergency: 'Proceeds now to protect service. Reviewed afterwards.',
};

const typeColours: Record<ChangeType, ChipColour> = {
  Standard: 'default',
  Normal: 'info',

  // Emergency is coloured to stand out in a list. A rising count of these means the normal
  // process is not working for people, and it should be visible without reading the numbers.
  Emergency: 'error',
};

export function ChangeTypeChip({ type }: { type: ChangeType }) {
  return (
    <Tooltip title={typeHelp[type] ?? ''}>
      <Chip
        label={typeLabels[type] ?? type}
        color={typeColours[type] ?? 'default'}
        size="small"
        variant={type === 'Emergency' ? 'filled' : 'outlined'}
      />
    </Tooltip>
  );
}

const riskLabels: Record<ChangeRisk, string> = {
  Low: 'Low',
  Medium: 'Medium',
  High: 'High',
  VeryHigh: 'Very high',
};

const riskColours: Record<ChangeRisk, ChipColour> = {
  Low: 'success',
  Medium: 'default',
  High: 'warning',
  VeryHigh: 'error',
};

export function ChangeRiskChip({ risk }: { risk: ChangeRisk }) {
  return (
    <Chip
      label={riskLabels[risk] ?? risk}
      color={riskColours[risk] ?? 'default'}
      size="small"
      variant={risk === 'VeryHigh' ? 'filled' : 'outlined'}
    />
  );
}

const statusLabels: Record<ChangeStatus, string> = {
  Draft: 'Draft',
  Assessing: 'Assessing',
  AwaitingApproval: 'Awaiting approval',
  Scheduled: 'Scheduled',
  Implementing: 'Implementing',
  Review: 'Awaiting review',
  Closed: 'Closed',
  Rejected: 'Rejected',
  Cancelled: 'Cancelled',
};

const statusColours: Record<ChangeStatus, ChipColour> = {
  Draft: 'default',
  Assessing: 'default',
  AwaitingApproval: 'warning',
  Scheduled: 'info',
  Implementing: 'primary',
  Review: 'warning',
  Closed: 'success',
  Rejected: 'error',
  Cancelled: 'default',
};

export function ChangeStatusChip({
  status,
  size = 'small',
}: {
  status: ChangeStatus;
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

const outcomeLabels: Record<ChangeOutcome, string> = {
  Successful: 'Successful',
  SuccessfulWithIssues: 'Successful with issues',
  Failed: 'Failed',
  RolledBack: 'Rolled back',
};

const outcomeColours: Record<ChangeOutcome, ChipColour> = {
  Successful: 'success',
  SuccessfulWithIssues: 'warning',
  Failed: 'error',
  RolledBack: 'warning',
};

export function ChangeOutcomeChip({ outcome }: { outcome: ChangeOutcome }) {
  return (
    <Chip
      label={outcomeLabels[outcome] ?? outcome}
      color={outcomeColours[outcome] ?? 'default'}
      size="small"
    />
  );
}
