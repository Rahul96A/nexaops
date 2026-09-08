import { Chip, Tooltip } from '@mui/material';
import type { WorkflowRunStatus, WorkflowStepStatus, WorkflowTrigger } from '@/api/types';

type ChipColour = 'default' | 'primary' | 'secondary' | 'error' | 'info' | 'success' | 'warning';

const triggerLabels: Record<WorkflowTrigger, string> = {
  RecordCreated: 'When raised',
  StatusChanged: 'When status changes',
  PriorityChanged: 'When priority changes',
  AssignmentChanged: 'When reassigned',
};

export function TriggerChip({ trigger }: { trigger: WorkflowTrigger }) {
  return <Chip label={triggerLabels[trigger] ?? trigger} size="small" variant="outlined" />;
}

const runStatusLabels: Record<WorkflowRunStatus, string> = {
  Skipped: 'Did not match',
  Succeeded: 'Ran',
  PartiallyCompleted: 'Partly ran',
  Failed: 'Failed',
  Suppressed: 'Not run',
};

const runStatusHelp: Record<WorkflowRunStatus, string> = {
  Skipped: 'The record did not meet the rule’s conditions. Open the run to see which one.',
  Succeeded: 'Every action completed.',
  PartiallyCompleted:
    'Some actions completed and some did not. The record still changed — open the run to see what failed.',
  Failed: 'Every action failed. The record itself was not affected.',
  Suppressed:
    'Another rule’s action caused this change. Automation does not trigger automation, so this rule was not run.',
};

const runStatusColours: Record<WorkflowRunStatus, ChipColour> = {
  Skipped: 'default',
  Succeeded: 'success',
  PartiallyCompleted: 'warning',
  Failed: 'error',
  Suppressed: 'info',
};

export function RunStatusChip({ status }: { status: WorkflowRunStatus }) {
  return (
    <Tooltip title={runStatusHelp[status] ?? ''}>
      <Chip
        label={runStatusLabels[status] ?? status}
        color={runStatusColours[status] ?? 'default'}
        size="small"
        variant={status === 'Skipped' || status === 'Suppressed' ? 'outlined' : 'filled'}
      />
    </Tooltip>
  );
}

const stepStatusColours: Record<WorkflowStepStatus, ChipColour> = {
  Succeeded: 'success',
  Skipped: 'default',
  Failed: 'error',
};

const stepStatusLabels: Record<WorkflowStepStatus, string> = {
  Succeeded: 'Done',
  Skipped: 'Nothing to do',
  Failed: 'Failed',
};

export function StepStatusChip({ status }: { status: WorkflowStepStatus }) {
  return (
    <Chip
      label={stepStatusLabels[status] ?? status}
      color={stepStatusColours[status] ?? 'default'}
      size="small"
      variant={status === 'Skipped' ? 'outlined' : 'filled'}
    />
  );
}
