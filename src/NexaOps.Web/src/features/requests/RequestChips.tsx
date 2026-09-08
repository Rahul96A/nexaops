import { Chip } from '@mui/material';
import type { ApprovalState, RequestItemStatus, RequestStatus } from '@/api/types';

/**
 * Request vocabulary, kept separate from the incident chips because the words differ and
 * sharing one component would force one module's language onto the other.
 */

type ChipColour = 'default' | 'primary' | 'secondary' | 'error' | 'info' | 'success' | 'warning';

const requestStatusLabels: Record<RequestStatus, string> = {
  Draft: 'Draft',
  AwaitingApproval: 'Awaiting approval',
  Approved: 'Approved',
  InProgress: 'In progress',
  Pending: 'On hold',
  Fulfilled: 'Delivered',
  Closed: 'Closed',
  Rejected: 'Rejected',
  Cancelled: 'Cancelled',
};

const requestStatusColours: Record<RequestStatus, ChipColour> = {
  Draft: 'default',
  AwaitingApproval: 'warning',
  Approved: 'info',
  InProgress: 'primary',
  Pending: 'default',
  Fulfilled: 'success',
  Closed: 'default',
  Rejected: 'error',
  Cancelled: 'default',
};

export function RequestStatusChip({
  status,
  size = 'small',
}: {
  status: RequestStatus;
  size?: 'small' | 'medium';
}) {
  return (
    <Chip
      label={requestStatusLabels[status] ?? status}
      color={requestStatusColours[status] ?? 'default'}
      size={size}
      variant={status === 'Closed' || status === 'Cancelled' ? 'outlined' : 'filled'}
    />
  );
}

const itemStatusLabels: Record<RequestItemStatus, string> = {
  Pending: 'Not started',
  InProgress: 'In progress',
  Fulfilled: 'Delivered',
  Cancelled: 'Cancelled',
};

const itemStatusColours: Record<RequestItemStatus, ChipColour> = {
  Pending: 'default',
  InProgress: 'primary',
  Fulfilled: 'success',
  Cancelled: 'default',
};

export function RequestItemStatusChip({ status }: { status: RequestItemStatus }) {
  return (
    <Chip
      label={itemStatusLabels[status] ?? status}
      color={itemStatusColours[status] ?? 'default'}
      size="small"
      variant={status === 'Fulfilled' ? 'filled' : 'outlined'}
    />
  );
}

const approvalLabels: Record<ApprovalState, string> = {
  Pending: 'Waiting',
  Approved: 'Approved',
  Rejected: 'Rejected',
  Cancelled: 'Withdrawn',

  // "Somebody else decided first" is a different fact from "the request went away", and an
  // approver looking at their own history is entitled to see which one applied.
  NotRequired: 'Not needed',
};

const approvalColours: Record<ApprovalState, ChipColour> = {
  Pending: 'warning',
  Approved: 'success',
  Rejected: 'error',
  Cancelled: 'default',
  NotRequired: 'default',
};

export function ApprovalStateChip({ state }: { state: ApprovalState }) {
  return (
    <Chip
      label={approvalLabels[state] ?? state}
      color={approvalColours[state] ?? 'default'}
      size="small"
      variant={state === 'Pending' ? 'filled' : 'outlined'}
    />
  );
}
