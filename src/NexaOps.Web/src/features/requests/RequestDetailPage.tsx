import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Divider,
  LinearProgress,
  List,
  ListItem,
  ListItemText,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import { requestsApi } from '@/api/requests';
import { ApiError } from '@/api/client';
import type { RequestDetail, RequestStatus } from '@/api/types';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { PageHeader } from '@/components/PageHeader';
import { ErrorState } from '@/components/ErrorState';
import { PriorityChip } from '@/components/StatusChips';
import { ApprovalStateChip, RequestItemStatusChip, RequestStatusChip } from './RequestChips';
import { formatCurrency, formatDateTime, formatRelative } from '@/utils/format';

/** Labels for the transitions the server says are currently legal. */
const transitionLabels: Partial<Record<RequestStatus, string>> = {
  InProgress: 'Start work',
  Pending: 'Put on hold',
  Fulfilled: 'Mark delivered',
  Closed: 'Close',
};

export function RequestDetailPage() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const [comment, setComment] = useState('');
  const [workNote, setWorkNote] = useState(false);

  const { data: request, isLoading, error, refetch } = useQuery({
    queryKey: ['request', id],
    queryFn: ({ signal }) => requestsApi.get(id, signal),
    enabled: Boolean(id),
  });

  const { data: comments } = useQuery({
    queryKey: ['request-comments', id],
    queryFn: ({ signal }) => requestsApi.comments(id, signal),
    enabled: Boolean(id),
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['request', id] });
    void queryClient.invalidateQueries({ queryKey: ['request-comments', id] });
    void queryClient.invalidateQueries({ queryKey: ['requests'] });
  };

  const changeStatus = useMutation({
    mutationFn: (status: RequestStatus) => requestsApi.changeStatus(id, { status }),
    onSuccess: invalidate,
  });

  const fulfilItem = useMutation({
    mutationFn: (itemId: string) => requestsApi.fulfilItem(id, itemId, {}),
    onSuccess: invalidate,
  });

  const addComment = useMutation({
    mutationFn: () =>
      requestsApi.addComment(id, {
        body: comment,
        kind: workNote ? 'WorkNote' : 'PublicComment',
      }),
    onSuccess: () => {
      setComment('');
      invalidate();
    },
  });

  if (isLoading) {
    return <LinearProgress />;
  }

  if (error) {
    return <ErrorState error={error} onRetry={() => void refetch()} />;
  }

  if (!request) {
    return null;
  }

  return (
    <Box>
      <PageHeader
        title={request.number}
        subtitle={request.title}
        crumbs={[{ label: 'Service requests', to: '/requests' }, { label: request.number }]}
        actions={
          <Stack direction="row" gap={1}>
            {request.allowedTransitions
              .filter((status) => transitionLabels[status])
              .map((status) => (
                <Button
                  key={status}
                  variant={status === 'Fulfilled' ? 'contained' : 'outlined'}
                  disabled={changeStatus.isPending}
                  onClick={() => changeStatus.mutate(status)}
                >
                  {transitionLabels[status]}
                </Button>
              ))}
          </Stack>
        }
      />

      {changeStatus.error instanceof ApiError && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          {changeStatus.error.userMessage}
        </Alert>
      )}

      {request.status === 'Rejected' && request.rejectionReason && (
        <Alert severity="error" sx={{ mb: 2 }}>
          <strong>Not approved.</strong> {request.rejectionReason}
        </Alert>
      )}

      {request.status === 'Cancelled' && (
        <Alert severity="info" sx={{ mb: 2 }}>
          Cancelled. {request.cancellationReason}
        </Alert>
      )}

      <Stack direction={{ xs: 'column', lg: 'row' }} gap={3} alignItems="flex-start">
        <Stack gap={3} sx={{ flex: 1, width: '100%' }}>
          <ItemsCard request={request} onFulfil={(itemId) => fulfilItem.mutate(itemId)} />

          {request.approvals.length > 0 && <ApprovalsCard request={request} />}

          <Card variant="outlined">
            <CardContent>
              <Typography variant="subtitle2" gutterBottom>
                Activity
              </Typography>

              <Can permission={Permissions.requestCommentCreate}>
                <Stack gap={1} sx={{ mb: 2 }}>
                  <TextField
                    multiline
                    minRows={2}
                    size="small"
                    placeholder={workNote ? 'Internal work note…' : 'Reply to the requester…'}
                    value={comment}
                    onChange={(event) => setComment(event.target.value)}
                  />
                  <Stack direction="row" gap={1} alignItems="center">
                    <Button
                      size="small"
                      variant="contained"
                      disabled={!comment.trim() || addComment.isPending}
                      onClick={() => addComment.mutate()}
                    >
                      Post
                    </Button>

                    <Can permission={Permissions.requestWorkNoteCreate}>
                      <Button size="small" onClick={() => setWorkNote((value) => !value)}>
                        {workNote ? 'Switch to reply' : 'Switch to work note'}
                      </Button>
                    </Can>
                  </Stack>
                </Stack>
              </Can>

              <List dense disablePadding>
                {(comments ?? []).map((entry) => (
                  <ListItem key={entry.id} disableGutters alignItems="flex-start">
                    <ListItemText
                      primary={
                        <Stack direction="row" gap={1} alignItems="center">
                          <Typography variant="body2" fontWeight={600}>
                            {entry.authorDisplayName}
                          </Typography>
                          {entry.kind === 'WorkNote' && (
                            <Chip label="Internal" size="small" color="warning" variant="outlined" />
                          )}
                          <Typography variant="caption" color="text.secondary">
                            {formatRelative(entry.createdAt)}
                          </Typography>
                        </Stack>
                      }
                      secondary={
                        <Typography variant="body2" sx={{ whiteSpace: 'pre-line' }}>
                          {entry.body}
                        </Typography>
                      }
                    />
                  </ListItem>
                ))}

                {(comments ?? []).length === 0 && (
                  <Typography variant="body2" color="text.secondary">
                    Nothing has been posted on this request yet.
                  </Typography>
                )}
              </List>
            </CardContent>
          </Card>
        </Stack>

        <Card variant="outlined" sx={{ width: { xs: '100%', lg: 320 }, flexShrink: 0 }}>
          <CardContent>
            <Stack gap={1.5}>
              <Field label="Status">
                <RequestStatusChip status={request.status} />
              </Field>
              <Field label="Priority">
                <PriorityChip priority={request.priority} />
              </Field>
              <Field label="Requested by">{request.requesterName}</Field>
              <Field label="Requested for">{request.requestedForName}</Field>
              <Field label="Fulfilment group">{request.fulfilmentGroupName ?? '—'}</Field>
              <Field label="Assignee">{request.assignedToName ?? 'Unassigned'}</Field>

              {request.totalCost != null && (
                <Field label="Indicative cost">{formatCurrency(request.totalCost)}</Field>
              )}

              <Divider />

              <Field label="Raised">{formatDateTime(request.createdAt)}</Field>
              {request.approvedAt && <Field label="Approved">{formatDateTime(request.approvedAt)}</Field>}
              {request.fulfilledAt && (
                <Field label="Delivered">{formatDateTime(request.fulfilledAt)}</Field>
              )}
            </Stack>
          </CardContent>
        </Card>
      </Stack>
    </Box>
  );
}

function ItemsCard({
  request,
  onFulfil,
}: {
  request: RequestDetail;
  onFulfil: (itemId: string) => void;
}) {
  const authorised = request.status !== 'Draft' && request.status !== 'AwaitingApproval';

  return (
    <Card variant="outlined">
      <CardContent>
        <Typography variant="subtitle2" gutterBottom>
          Ordered items
        </Typography>

        <Stack gap={2}>
          {request.items.map((item) => (
            <Box key={item.id}>
              <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
                <Stack direction="row" gap={1} alignItems="center">
                  <Typography variant="body2" fontWeight={600}>
                    {item.catalogItemName}
                  </Typography>
                  {item.quantity > 1 && <Chip label={`× ${item.quantity}`} size="small" />}
                  <RequestItemStatusChip status={item.status} />
                </Stack>

                <Stack direction="row" gap={1} alignItems="center">
                  {item.lineCost != null && (
                    <Typography variant="body2">{formatCurrency(item.lineCost)}</Typography>
                  )}

                  {item.status !== 'Fulfilled' && item.status !== 'Cancelled' && (
                    <Can permission={Permissions.requestFulfil}>
                      <Button
                        size="small"
                        startIcon={<CheckCircleOutlineIcon />}
                        disabled={!authorised}
                        title={
                          authorised
                            ? undefined
                            : 'This request has not been authorised yet.'
                        }
                        onClick={() => onFulfil(item.id)}
                      >
                        Mark delivered
                      </Button>
                    </Can>
                  )}
                </Stack>
              </Stack>

              {Object.keys(item.values).length > 0 && (
                <Stack direction="row" gap={1} flexWrap="wrap" sx={{ mt: 0.75 }}>
                  {Object.entries(item.values).map(([key, value]) => (
                    <Chip key={key} label={`${key}: ${value}`} size="small" variant="outlined" />
                  ))}
                </Stack>
              )}

              {item.fulfilmentNotes && (
                <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.5 }}>
                  {item.fulfilmentNotes}
                </Typography>
              )}
            </Box>
          ))}
        </Stack>
      </CardContent>
    </Card>
  );
}

function ApprovalsCard({ request }: { request: RequestDetail }) {
  return (
    <Card variant="outlined">
      <CardContent>
        <Typography variant="subtitle2" gutterBottom>
          Approvals
        </Typography>

        <Stack gap={1.5}>
          {request.approvals.map((approval) => (
            <Stack
              key={approval.id}
              direction="row"
              justifyContent="space-between"
              alignItems="flex-start"
              gap={1}
            >
              <Box>
                <Typography variant="body2">
                  {approval.approverName ?? approval.approverGroupName ?? 'Unassigned approver'}
                </Typography>
                {approval.comment && (
                  <Typography variant="caption" color="text.secondary">
                    {approval.comment}
                  </Typography>
                )}
              </Box>

              <Stack alignItems="flex-end" gap={0.5}>
                <ApprovalStateChip state={approval.state} />
                {approval.decidedAt && (
                  <Typography variant="caption" color="text.secondary">
                    {formatRelative(approval.decidedAt)}
                  </Typography>
                )}
              </Stack>
            </Stack>
          ))}
        </Stack>
      </CardContent>
    </Card>
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
