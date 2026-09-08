import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Avatar,
  Box,
  Button,
  CardContent,
  Chip,
  Divider,
  Skeleton,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material';
import SendIcon from '@mui/icons-material/Send';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import ChatBubbleOutlineIcon from '@mui/icons-material/ChatBubbleOutline';
import EditNoteOutlinedIcon from '@mui/icons-material/EditNoteOutlined';
import { incidentsApi } from '@/api/incidents';
import { ApiError } from '@/api/client';
import type { IncidentCommentKind, IncidentDetail } from '@/api/types';
import { ErrorState } from '@/components/ErrorState';
import { EmptyState } from '@/components/EmptyState';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { useAuth } from '@/auth/useAuth';
import { formatLongDateTime, formatRelative, humanise, initials } from '@/utils/format';

/**
 * The incident conversation and history.
 *
 * Public comments and internal work notes are visually distinct, because posting an internal
 * note into a customer-visible thread is the mistake this screen most needs to prevent. The
 * composer shows which of the two it will write before the agent types anything.
 */
export function IncidentActivityTab({
  incident,
  onChanged,
}: {
  incident: IncidentDetail;
  onChanged: () => void;
}) {
  const queryClient = useQueryClient();
  const { profile, hasPermission } = useAuth();

  const canWriteWorkNote = hasPermission(Permissions.incidentWorkNoteCreate);

  const [kind, setKind] = useState<IncidentCommentKind>('PublicComment');
  const [body, setBody] = useState('');
  const [error, setError] = useState<string | null>(null);

  const activity = useQuery({
    queryKey: ['incidents', 'activity', incident.id],
    queryFn: ({ signal }) => incidentsApi.activity(incident.id, signal),
  });

  const addComment = useMutation({
    mutationFn: () => incidentsApi.addComment(incident.id, { body: body.trim(), kind }),
    onSuccess: () => {
      setBody('');
      setError(null);
      void queryClient.invalidateQueries({ queryKey: ['incidents', 'activity', incident.id] });
      onChanged();
    },
    onError: (mutationError) => {
      setError(
        mutationError instanceof ApiError
          ? mutationError.userMessage
          : 'The comment could not be saved.',
      );
    },
  });

  const isClosed = incident.status === 'Closed' || incident.status === 'Cancelled';

  return (
    <CardContent>
      <Can permission={Permissions.incidentCommentCreate}>
        <Box sx={{ mb: 3 }}>
          {isClosed ? (
            <Alert severity="info">
              This incident is {incident.status.toLowerCase()} and no longer accepts comments.
              Raise a new incident that references {incident.number} if the problem returns.
            </Alert>
          ) : (
            <>
              {canWriteWorkNote && (
                <ToggleButtonGroup
                  value={kind}
                  exclusive
                  size="small"
                  onChange={(_, value: IncidentCommentKind | null) => value && setKind(value)}
                  sx={{ mb: 1.5 }}
                  aria-label="Comment visibility"
                >
                  <ToggleButton value="PublicComment">
                    <ChatBubbleOutlineIcon sx={{ fontSize: 16, mr: 0.75 }} />
                    Reply to requester
                  </ToggleButton>
                  <ToggleButton value="WorkNote">
                    <LockOutlinedIcon sx={{ fontSize: 16, mr: 0.75 }} />
                    Internal work note
                  </ToggleButton>
                </ToggleButtonGroup>
              )}

              {error && (
                <Alert severity="error" sx={{ mb: 1.5 }} onClose={() => setError(null)}>
                  {error}
                </Alert>
              )}

              <Stack direction="row" spacing={1.5} alignItems="flex-start">
                <Avatar
                  sx={{
                    width: 34,
                    height: 34,
                    fontSize: 14,
                    fontWeight: 700,
                    bgcolor: profile?.avatarColor ?? 'primary.main',
                  }}
                >
                  {initials(profile?.displayName)}
                </Avatar>

                <Box sx={{ flex: 1 }}>
                  <TextField
                    value={body}
                    onChange={(event) => setBody(event.target.value)}
                    placeholder={
                      kind === 'WorkNote'
                        ? 'Internal note — not visible to the requester'
                        : 'Reply to the requester…'
                    }
                    multiline
                    minRows={3}
                    fullWidth
                    sx={{
                      '& .MuiOutlinedInput-root': {
                        bgcolor: kind === 'WorkNote' ? 'warning.50' : 'background.paper',
                      },
                    }}
                  />

                  <Stack direction="row" spacing={1} justifyContent="space-between" sx={{ mt: 1 }}>
                    <Typography variant="caption" color="text.secondary">
                      {kind === 'WorkNote'
                        ? 'Only people with permission to read work notes will see this.'
                        : 'The requester will be notified.'}
                    </Typography>

                    <Button
                      variant="contained"
                      size="small"
                      startIcon={<SendIcon />}
                      disabled={body.trim().length === 0 || addComment.isPending}
                      onClick={() => addComment.mutate()}
                    >
                      {addComment.isPending ? 'Posting…' : 'Post'}
                    </Button>
                  </Stack>
                </Box>
              </Stack>
            </>
          )}
        </Box>

        <Divider sx={{ mb: 3 }} />
      </Can>

      {activity.isLoading && (
        <Stack spacing={2}>
          {Array.from({ length: 4 }, (_, index) => (
            <Skeleton key={index} variant="rounded" height={72} />
          ))}
        </Stack>
      )}

      {activity.isError && <ErrorState error={activity.error} onRetry={() => activity.refetch()} />}

      {activity.data && activity.data.length === 0 && (
        <EmptyState
          title="No activity yet"
          description="Comments, work notes and field changes appear here as the incident progresses."
          icon={<EditNoteOutlinedIcon />}
        />
      )}

      <Stack spacing={0}>
        {(activity.data ?? []).map((entry, index) => {
          const isWorkNote = entry.type === 'work_note';
          const isChange = entry.type === 'field_change';

          return (
            <Box
              key={`${entry.id}-${index}`}
              sx={{
                display: 'flex',
                gap: 1.5,
                pb: 3,
                position: 'relative',
                // A continuous rail down the timeline, stopping at the last entry.
                '&::before':
                  index < (activity.data?.length ?? 0) - 1
                    ? {
                        content: '""',
                        position: 'absolute',
                        left: 17,
                        top: 38,
                        bottom: 0,
                        width: '2px',
                        bgcolor: 'divider',
                      }
                    : undefined,
              }}
            >
              <Avatar
                sx={{
                  width: 36,
                  height: 36,
                  fontSize: 13,
                  fontWeight: 700,
                  bgcolor: isChange
                    ? 'action.selected'
                    : (entry.actorAvatarColor ?? 'primary.main'),
                  color: isChange ? 'text.secondary' : undefined,
                  zIndex: 1,
                }}
              >
                {isChange ? <EditNoteOutlinedIcon sx={{ fontSize: 18 }} /> : initials(entry.actorName)}
              </Avatar>

              <Box sx={{ flex: 1, minWidth: 0 }}>
                <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                  <Typography variant="body2" sx={{ fontWeight: 600 }}>
                    {entry.actorName ?? 'System'}
                  </Typography>

                  {isWorkNote && (
                    <Chip
                      icon={<LockOutlinedIcon sx={{ fontSize: 13 }} />}
                      label="Internal"
                      size="small"
                      sx={{ bgcolor: 'warning.light', color: 'warning.contrastText', fontWeight: 700 }}
                    />
                  )}

                  {entry.isSystemGenerated && (
                    <Chip label="Automated" size="small" variant="outlined" />
                  )}

                  <Typography
                    variant="caption"
                    color="text.secondary"
                    title={formatLongDateTime(entry.occurredAt)}
                  >
                    {formatRelative(entry.occurredAt)}
                  </Typography>
                </Stack>

                {entry.body && (
                  <Box
                    sx={{
                      mt: 0.75,
                      p: 1.5,
                      borderRadius: 2,
                      bgcolor: isWorkNote ? 'warning.50' : 'action.hover',
                      border: 1,
                      borderColor: isWorkNote ? 'warning.light' : 'transparent',
                    }}
                  >
                    <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.6 }}>
                      {entry.body}
                    </Typography>
                  </Box>
                )}

                {isChange && entry.changes && entry.changes.length > 0 && (
                  <Stack spacing={0.5} sx={{ mt: 0.75 }}>
                    {entry.changes.map((change) => (
                      <Typography key={change.field} variant="caption" color="text.secondary">
                        <strong>{humanise(change.field)}</strong>{' '}
                        {change.from ? (
                          <>
                            changed from <em>{truncate(change.from)}</em> to{' '}
                            <em>{truncate(change.to)}</em>
                          </>
                        ) : (
                          <>set to <em>{truncate(change.to)}</em></>
                        )}
                      </Typography>
                    ))}
                  </Stack>
                )}
              </Box>
            </Box>
          );
        })}
      </Stack>
    </CardContent>
  );
}

function truncate(value: string | null | undefined, max = 80): string {
  if (!value) {
    return 'empty';
  }

  const cleaned = value.replaceAll('"', '');
  return cleaned.length > max ? `${cleaned.slice(0, max)}…` : cleaned;
}
