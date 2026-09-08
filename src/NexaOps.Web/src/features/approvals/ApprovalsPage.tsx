import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  LinearProgress,
  Link,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material';
import CheckIcon from '@mui/icons-material/Check';
import CloseIcon from '@mui/icons-material/Close';
import { approvalsApi } from '@/api/requests';
import { ApiError } from '@/api/client';
import type { Approval } from '@/api/types';
import { PageHeader } from '@/components/PageHeader';
import { EmptyState } from '@/components/EmptyState';
import { ErrorState } from '@/components/ErrorState';
import { ApprovalStateChip } from '@/features/requests/RequestChips';
import { formatRelative } from '@/utils/format';

/**
 * The approver's queue.
 *
 * Only approvals addressed to the caller appear here - the server decides that, not this page.
 * A rejection requires a reason, which is enforced server-side and mirrored in the dialog.
 */
export function ApprovalsPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [outstandingOnly, setOutstandingOnly] = useState(true);
  const [rejecting, setRejecting] = useState<Approval | null>(null);
  const [reason, setReason] = useState('');

  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ['approvals', outstandingOnly],
    queryFn: ({ signal }) => approvalsApi.mine(outstandingOnly, signal),
  });

  const decide = useMutation({
    mutationFn: ({ id, approved, comment }: { id: string; approved: boolean; comment?: string }) =>
      approvalsApi.decide(id, { approved, comment }),
    onSuccess: () => {
      setRejecting(null);
      setReason('');
      void queryClient.invalidateQueries({ queryKey: ['approvals'] });
      void queryClient.invalidateQueries({ queryKey: ['requests'] });
    },
  });

  return (
    <Box>
      <PageHeader
        title="Approvals"
        subtitle="Requests waiting on your authorisation."
        actions={
          <ToggleButtonGroup
            exclusive
            size="small"
            value={outstandingOnly}
            onChange={(_, value: boolean | null) => value !== null && setOutstandingOnly(value)}
          >
            <ToggleButton value={true}>Waiting on me</ToggleButton>
            <ToggleButton value={false}>All</ToggleButton>
          </ToggleButtonGroup>
        }
      />

      {isLoading && <LinearProgress />}

      {error && <ErrorState error={error} onRetry={() => void refetch()} />}

      {decide.error instanceof ApiError && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          {decide.error.userMessage}
        </Alert>
      )}

      {data && data.length === 0 && (
        <EmptyState
          title={outstandingOnly ? 'Nothing waiting on you' : 'No approvals'}
          description={
            outstandingOnly
              ? 'When somebody raises a request that needs your authorisation, it will appear here.'
              : 'You have not been asked to approve anything yet.'
          }
        />
      )}

      <Stack gap={1.5}>
        {(data ?? []).map((approval) => (
          <Card key={approval.id} variant="outlined">
            <CardContent>
              <Stack
                direction={{ xs: 'column', sm: 'row' }}
                justifyContent="space-between"
                alignItems={{ xs: 'flex-start', sm: 'center' }}
                gap={1.5}
              >
                <Box>
                  <Stack direction="row" gap={1} alignItems="center">
                    <Typography variant="subtitle2">
                      {approval.recordNumber ? (
                        <Link
                          component="button"
                          underline="hover"
                          onClick={() => navigate(`/requests/${approval.recordId}`)}
                        >
                          {approval.recordLabel}
                        </Link>
                      ) : (
                        approval.recordLabel
                      )}
                    </Typography>
                    <ApprovalStateChip state={approval.state} />
                  </Stack>

                  <Typography variant="caption" color="text.secondary">
                    {approval.state === 'Pending'
                      ? 'Waiting on you'
                      : `Decided ${approval.decidedAt ? formatRelative(approval.decidedAt) : ''}`}
                    {approval.comment ? ` — ${approval.comment}` : ''}
                  </Typography>
                </Box>

                {approval.state === 'Pending' && (
                  <Stack direction="row" gap={1}>
                    <Button
                      size="small"
                      variant="contained"
                      startIcon={<CheckIcon />}
                      disabled={decide.isPending}
                      onClick={() => decide.mutate({ id: approval.id, approved: true })}
                    >
                      Approve
                    </Button>
                    <Button
                      size="small"
                      color="error"
                      variant="outlined"
                      startIcon={<CloseIcon />}
                      disabled={decide.isPending}
                      onClick={() => setRejecting(approval)}
                    >
                      Reject
                    </Button>
                  </Stack>
                )}
              </Stack>
            </CardContent>
          </Card>
        ))}
      </Stack>

      <Dialog open={rejecting !== null} onClose={() => setRejecting(null)} fullWidth maxWidth="sm">
        <DialogTitle>Reject {rejecting?.recordLabel}</DialogTitle>
        <DialogContent>
          <Typography variant="body2" color="text.secondary" gutterBottom>
            The requester is told why, so this is required.
          </Typography>
          <TextField
            autoFocus
            fullWidth
            multiline
            minRows={3}
            label="Reason"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            sx={{ mt: 1 }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setRejecting(null)}>Cancel</Button>
          <Button
            color="error"
            variant="contained"
            disabled={!reason.trim() || decide.isPending}
            onClick={() =>
              rejecting && decide.mutate({ id: rejecting.id, approved: false, comment: reason })
            }
          >
            Reject
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}
