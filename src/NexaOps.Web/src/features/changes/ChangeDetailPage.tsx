import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Divider,
  LinearProgress,
  List,
  ListItem,
  ListItemText,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import WarningAmberOutlinedIcon from '@mui/icons-material/WarningAmberOutlined';
import { changesApi } from '@/api/changes';
import { ApiError } from '@/api/client';
import type { ChangeOutcome, ChangeStatus } from '@/api/types';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { PageHeader } from '@/components/PageHeader';
import { ErrorState } from '@/components/ErrorState';
import { PriorityChip } from '@/components/StatusChips';
import { ApprovalStateChip } from '@/features/requests/RequestChips';
import { ChangeOutcomeChip, ChangeRiskChip, ChangeStatusChip, ChangeTypeChip } from './ChangeChips';
import { formatDateTime } from '@/utils/format';

const transitionLabels: Partial<Record<ChangeStatus, string>> = {
  Assessing: 'Assess',
  Scheduled: 'Schedule',
  Implementing: 'Start implementing',
  Review: 'Finish implementing',
  Closed: 'Close',
};

const outcomes: ChangeOutcome[] = [
  'Successful',
  'SuccessfulWithIssues',
  'Failed',
  'RolledBack',
];

export function ChangeDetailPage() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();

  const [outcome, setOutcome] = useState<ChangeOutcome>('Successful');
  const [reviewNotes, setReviewNotes] = useState('');

  const { data: change, isLoading, error, refetch } = useQuery({
    queryKey: ['change', id],
    queryFn: ({ signal }) => changesApi.get(id, signal),
    enabled: Boolean(id),
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['change', id] });
    void queryClient.invalidateQueries({ queryKey: ['changes'] });
  };

  const changeStatus = useMutation({
    mutationFn: (status: ChangeStatus) => changesApi.changeStatus(id, { status }),
    onSuccess: invalidate,
  });

  const submit = useMutation({
    mutationFn: () => changesApi.submit(id),
    onSuccess: invalidate,
  });

  const review = useMutation({
    mutationFn: () => changesApi.review(id, { outcome, notes: reviewNotes }),
    onSuccess: () => {
      setReviewNotes('');
      invalidate();
    },
  });

  if (isLoading) {
    return <LinearProgress />;
  }

  if (error) {
    return <ErrorState error={error} onRetry={() => void refetch()} />;
  }

  if (!change) {
    return null;
  }

  const mutationError = [changeStatus.error, submit.error, review.error].find(
    (e): e is ApiError => e instanceof ApiError,
  );

  return (
    <Box>
      <PageHeader
        title={change.number}
        subtitle={change.title}
        crumbs={[{ label: 'Changes', to: '/changes' }, { label: change.number }]}
        actions={
          <Stack direction="row" gap={1} flexWrap="wrap">
            {change.type === 'Normal' && change.status !== 'AwaitingApproval' && (
              <Can permission={Permissions.changeUpdate}>
                <Button
                  variant="outlined"
                  disabled={submit.isPending || change.status !== 'Draft'}
                  onClick={() => submit.mutate()}
                >
                  Submit to board
                </Button>
              </Can>
            )}

            {change.allowedTransitions
              .filter((status) => transitionLabels[status])
              .map((status) => (
                <Button
                  key={status}
                  variant={status === 'Scheduled' ? 'contained' : 'outlined'}
                  disabled={changeStatus.isPending}
                  onClick={() => changeStatus.mutate(status)}
                >
                  {transitionLabels[status]}
                </Button>
              ))}
          </Stack>
        }
      />

      {mutationError && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          {mutationError.userMessage}
        </Alert>
      )}

      {change.status === 'Rejected' && change.rejectionReason && (
        <Alert severity="error" sx={{ mb: 2 }}>
          <strong>Not approved.</strong> {change.rejectionReason}
        </Alert>
      )}

      {change.collidingChanges.length > 0 && (
        <Alert icon={<WarningAmberOutlinedIcon />} severity="warning" sx={{ mb: 2 }}>
          <strong>
            {change.collidingChanges.length} other change
            {change.collidingChanges.length === 1 ? '' : 's'} share this window.
          </strong>{' '}
          {change.collidingChanges.map((c) => c.number).join(', ')} — this is a warning, not a
          block.
        </Alert>
      )}

      <Stack direction={{ xs: 'column', lg: 'row' }} gap={3} alignItems="flex-start">
        <Stack gap={3} sx={{ flex: 1, width: '100%' }}>
          <Card variant="outlined">
            <CardContent>
              <Typography variant="subtitle2" gutterBottom>
                Plans
              </Typography>

              <Stack gap={2}>
                <Plan label="Implementation" value={change.implementationPlan} />
                <Plan
                  label="Rollback"
                  value={change.rollbackPlan}
                  missingNote={
                    change.risk === 'Low'
                      ? 'Not required for a low-risk change.'
                      : 'Required before this change can proceed.'
                  }
                />
                <Plan label="Test" value={change.testPlan} />
                <Plan label="Impact assessment" value={change.impactAssessment} />
              </Stack>
            </CardContent>
          </Card>

          {change.approvals.length > 0 && (
            <Card variant="outlined">
              <CardContent>
                <Typography variant="subtitle2" gutterBottom>
                  Approvals
                </Typography>

                <List dense disablePadding>
                  {change.approvals.map((approval) => (
                    <ListItem key={approval.id} disableGutters>
                      <ListItemText
                        primary={
                          <Stack direction="row" gap={1} alignItems="center">
                            <Typography variant="body2">
                              {approval.approverGroupName ?? approval.approverName ?? 'Approver'}
                            </Typography>
                            <ApprovalStateChip state={approval.state} />
                          </Stack>
                        }
                        secondary={approval.comment ?? undefined}
                      />
                    </ListItem>
                  ))}
                </List>
              </CardContent>
            </Card>
          )}

          {change.status === 'Review' && (
            <Card variant="outlined">
              <CardContent>
                <Typography variant="subtitle2" gutterBottom>
                  Post-implementation review
                </Typography>

                <Can
                  permission={Permissions.changeReview}
                  fallback={
                    <Typography variant="body2" color="text.secondary">
                      This change is waiting on a reviewer.
                    </Typography>
                  }
                >
                  <Stack gap={2}>
                    <TextField
                      label="Outcome"
                      select
                      value={outcome}
                      onChange={(event) => setOutcome(event.target.value as ChangeOutcome)}
                      sx={{ maxWidth: 280 }}
                    >
                      {outcomes.map((option) => (
                        <MenuItem key={option} value={option}>
                          {option}
                        </MenuItem>
                      ))}
                    </TextField>

                    <TextField
                      label="What actually happened"
                      multiline
                      minRows={3}
                      value={reviewNotes}
                      onChange={(event) => setReviewNotes(event.target.value)}
                      helperText="Required whatever the outcome — a change with no note is indistinguishable from one nobody reviewed."
                    />

                    <Box>
                      <Button
                        variant="contained"
                        disabled={!reviewNotes.trim() || review.isPending}
                        onClick={() => review.mutate()}
                      >
                        Record review
                      </Button>
                    </Box>
                  </Stack>
                </Can>
              </CardContent>
            </Card>
          )}

          {change.reviewNotes && (
            <Card variant="outlined">
              <CardContent>
                <Stack direction="row" gap={1} alignItems="center" sx={{ mb: 1 }}>
                  <Typography variant="subtitle2">Review</Typography>
                  {change.outcome && <ChangeOutcomeChip outcome={change.outcome} />}
                </Stack>
                <Typography variant="body2" sx={{ whiteSpace: 'pre-line' }}>
                  {change.reviewNotes}
                </Typography>
              </CardContent>
            </Card>
          )}
        </Stack>

        <Card variant="outlined" sx={{ width: { xs: '100%', lg: 320 }, flexShrink: 0 }}>
          <CardContent>
            <Stack gap={1.5}>
              <Field label="Status">
                <ChangeStatusChip status={change.status} />
              </Field>
              <Field label="Type">
                <ChangeTypeChip type={change.type} />
              </Field>
              <Field label="Risk">
                <ChangeRiskChip risk={change.risk} />
              </Field>
              <Field label="Priority">
                <PriorityChip priority={change.priority} />
              </Field>

              <Divider />

              <Field label="Requested by">{change.requestedByName}</Field>
              <Field label="Assignee">{change.assignedToName ?? 'Unassigned'}</Field>
              <Field label="Group">{change.assignmentGroupName ?? '—'}</Field>
              <Field label="Needs downtime">{change.requiresDowntime ? 'Yes' : 'No'}</Field>

              {change.problemNumber && <Field label="Fixes problem">{change.problemNumber}</Field>}

              <Divider />

              <Field label="Planned start">
                {change.plannedStartAt ? formatDateTime(change.plannedStartAt) : 'Not scheduled'}
              </Field>
              <Field label="Planned end">
                {change.plannedEndAt ? formatDateTime(change.plannedEndAt) : '—'}
              </Field>

              {change.actualStartAt && (
                <Field label="Actual start">{formatDateTime(change.actualStartAt)}</Field>
              )}
              {change.actualEndAt && (
                <Field label="Actual end">{formatDateTime(change.actualEndAt)}</Field>
              )}
            </Stack>
          </CardContent>
        </Card>
      </Stack>
    </Box>
  );
}

function Plan({
  label,
  value,
  missingNote = 'Not recorded yet.',
}: {
  label: string;
  value?: string | null;
  missingNote?: string;
}) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography
        variant="body2"
        color={value?.trim() ? 'text.primary' : 'text.secondary'}
        sx={{ whiteSpace: 'pre-line' }}
      >
        {value?.trim() ? value : missingNote}
      </Typography>
    </Box>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Box sx={{ textAlign: 'right' }}>
        {typeof children === 'string' ? (
          <Typography variant="body2">{children}</Typography>
        ) : (
          children
        )}
      </Box>
    </Stack>
  );
}
