import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
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
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import LightbulbOutlinedIcon from '@mui/icons-material/LightbulbOutlined';
import { problemsApi } from '@/api/problems';
import { ApiError } from '@/api/client';
import type { ProblemStatus, RootCauseConfidence } from '@/api/types';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { PageHeader } from '@/components/PageHeader';
import { ErrorState } from '@/components/ErrorState';
import { PriorityChip, StatusChip } from '@/components/StatusChips';
import { ConfidenceChip, ProblemStatusChip } from './ProblemChips';
import { formatDateTime, formatRelative } from '@/utils/format';

const transitionLabels: Partial<Record<ProblemStatus, string>> = {
  Investigating: 'Start investigating',
  KnownError: 'Publish known error',
  FixInProgress: 'Fix in progress',
  Resolved: 'Resolve',
  Closed: 'Close',
  Cancelled: 'Cancel',
};

const confidences: RootCauseConfidence[] = ['Suspected', 'Probable', 'Confirmed'];

export function ProblemDetailPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [rootCause, setRootCause] = useState('');
  const [workaround, setWorkaround] = useState('');
  const [permanentFix, setPermanentFix] = useState('');
  const [confidence, setConfidence] = useState<RootCauseConfidence>('Suspected');
  const [note, setNote] = useState('');

  const { data: problem, isLoading, error, refetch } = useQuery({
    queryKey: ['problem', id],
    queryFn: ({ signal }) => problemsApi.get(id, signal),
    enabled: Boolean(id),
  });

  const { data: comments } = useQuery({
    queryKey: ['problem-comments', id],
    queryFn: ({ signal }) => problemsApi.comments(id, signal),
    enabled: Boolean(id),
  });

  // The findings form is seeded from the record, so an investigator edits what is there rather
  // than retyping it and accidentally blanking a field.
  useEffect(() => {
    if (problem) {
      setRootCause(problem.rootCause ?? '');
      setWorkaround(problem.workaround ?? '');
      setPermanentFix(problem.permanentFix ?? '');
      setConfidence(problem.rootCauseConfidence ?? 'Suspected');
    }
  }, [problem]);

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['problem', id] });
    void queryClient.invalidateQueries({ queryKey: ['problem-comments', id] });
    void queryClient.invalidateQueries({ queryKey: ['problems'] });
  };

  const saveFindings = useMutation({
    mutationFn: () =>
      problemsApi.recordFindings(id, { rootCause, confidence, workaround, permanentFix }),
    onSuccess: invalidate,
  });

  const changeStatus = useMutation({
    mutationFn: (status: ProblemStatus) => problemsApi.changeStatus(id, { status }),
    onSuccess: invalidate,
  });

  const addComment = useMutation({
    mutationFn: () => problemsApi.addComment(id, { body: note, kind: 'WorkNote' }),
    onSuccess: () => {
      setNote('');
      invalidate();
    },
  });

  if (isLoading) {
    return <LinearProgress />;
  }

  if (error) {
    return <ErrorState error={error} onRetry={() => void refetch()} />;
  }

  if (!problem) {
    return null;
  }

  return (
    <Box>
      <PageHeader
        title={problem.number}
        subtitle={problem.title}
        crumbs={[{ label: 'Problems', to: '/problems' }, { label: problem.number }]}
        actions={
          <Stack direction="row" gap={1} flexWrap="wrap">
            {problem.allowedTransitions
              .filter((status) => transitionLabels[status])
              .map((status) => (
                <Button
                  key={status}
                  variant={status === 'KnownError' ? 'contained' : 'outlined'}
                  color={status === 'Cancelled' ? 'error' : 'primary'}
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

      {problem.status === 'KnownError' && problem.workaround && (
        <Alert icon={<LightbulbOutlinedIcon />} severity="success" sx={{ mb: 2 }}>
          <strong>Workaround published.</strong> {problem.workaround}
        </Alert>
      )}

      <Stack direction={{ xs: 'column', lg: 'row' }} gap={3} alignItems="flex-start">
        <Stack gap={3} sx={{ flex: 1, width: '100%' }}>
          <Card variant="outlined">
            <CardContent>
              <Typography variant="subtitle2" gutterBottom>
                Investigation
              </Typography>

              <Can
                permission={Permissions.problemInvestigate}
                fallback={
                  <Stack gap={2}>
                    <Finding label="Root cause" value={problem.rootCause} />
                    <Finding label="Workaround" value={problem.workaround} />
                    <Finding label="Permanent fix" value={problem.permanentFix} />
                  </Stack>
                }
              >
                <Stack gap={2}>
                  <TextField
                    label="Root cause"
                    multiline
                    minRows={2}
                    value={rootCause}
                    onChange={(event) => setRootCause(event.target.value)}
                    helperText="Required before the problem can be published as a known error."
                  />

                  <TextField
                    label="Confidence"
                    select
                    value={confidence}
                    onChange={(event) => setConfidence(event.target.value as RootCauseConfidence)}
                    sx={{ maxWidth: 240 }}
                  >
                    {confidences.map((option) => (
                      <MenuItem key={option} value={option}>
                        {option}
                      </MenuItem>
                    ))}
                  </TextField>

                  <TextField
                    label="Workaround"
                    multiline
                    minRows={2}
                    value={workaround}
                    onChange={(event) => setWorkaround(event.target.value)}
                    helperText="What the service desk should do in the meantime."
                  />

                  <TextField
                    label="Permanent fix"
                    multiline
                    minRows={2}
                    value={permanentFix}
                    onChange={(event) => setPermanentFix(event.target.value)}
                    helperText="Required before the problem can be resolved."
                  />

                  <Box>
                    <Button
                      variant="contained"
                      disabled={saveFindings.isPending}
                      onClick={() => saveFindings.mutate()}
                    >
                      {saveFindings.isPending ? 'Saving…' : 'Save findings'}
                    </Button>
                  </Box>
                </Stack>
              </Can>
            </CardContent>
          </Card>

          <Card variant="outlined">
            <CardContent>
              <Typography variant="subtitle2" gutterBottom>
                Incidents caused by this problem ({problem.linkedIncidentCount})
              </Typography>

              {problem.linkedIncidents.length === 0 && (
                <Typography variant="body2" color="text.secondary">
                  No incidents are attributed to this problem yet.
                </Typography>
              )}

              <List dense disablePadding>
                {problem.linkedIncidents.map((incident) => (
                  <ListItem
                    key={incident.id}
                    disableGutters
                    sx={{ cursor: 'pointer' }}
                    onClick={() => navigate(`/incidents/${incident.id}`)}
                  >
                    <ListItemText
                      primary={
                        <Stack direction="row" gap={1} alignItems="center">
                          <Typography variant="body2" fontFamily="monospace">
                            {incident.number}
                          </Typography>
                          <Typography variant="body2">{incident.title}</Typography>
                          <PriorityChip priority={incident.priority} />
                          <StatusChip status={incident.status} />
                        </Stack>
                      }
                    />
                  </ListItem>
                ))}
              </List>
            </CardContent>
          </Card>

          <Card variant="outlined">
            <CardContent>
              <Typography variant="subtitle2" gutterBottom>
                Investigation notes
              </Typography>

              <Can permission={Permissions.problemCommentCreate}>
                <Stack gap={1} sx={{ mb: 2 }}>
                  <TextField
                    multiline
                    minRows={2}
                    size="small"
                    placeholder="Add an investigation note…"
                    value={note}
                    onChange={(event) => setNote(event.target.value)}
                  />
                  <Box>
                    <Button
                      size="small"
                      variant="contained"
                      disabled={!note.trim() || addComment.isPending}
                      onClick={() => addComment.mutate()}
                    >
                      Post
                    </Button>
                  </Box>
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
                    Nothing recorded on this problem yet.
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
                <ProblemStatusChip status={problem.status} />
              </Field>
              <Field label="Priority">
                <PriorityChip priority={problem.priority} />
              </Field>

              {problem.rootCauseConfidence && (
                <Field label="Confidence">
                  <ConfidenceChip confidence={problem.rootCauseConfidence} />
                </Field>
              )}

              <Field label="Origin">{problem.origin}</Field>
              <Field label="Owner">{problem.ownerName ?? '—'}</Field>
              <Field label="Assignee">{problem.assignedToName ?? 'Unassigned'}</Field>
              <Field label="Group">{problem.assignmentGroupName ?? '—'}</Field>
              <Field label="Category">{problem.categoryName ?? '—'}</Field>

              <Divider />

              <Field label="Raised">{formatDateTime(problem.createdAt)}</Field>
              {problem.knownErrorAt && (
                <Field label="Known error">{formatDateTime(problem.knownErrorAt)}</Field>
              )}
              {problem.resolvedAt && (
                <Field label="Resolved">{formatDateTime(problem.resolvedAt)}</Field>
              )}
            </Stack>
          </CardContent>
        </Card>
      </Stack>
    </Box>
  );
}

function Finding({ label, value }: { label: string; value?: string | null }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="body2" sx={{ whiteSpace: 'pre-line' }}>
        {value?.trim() ? value : 'Not recorded yet.'}
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
