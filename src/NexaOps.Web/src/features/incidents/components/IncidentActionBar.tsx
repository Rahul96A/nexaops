import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import PersonAddAltOutlinedIcon from '@mui/icons-material/PersonAddAltOutlined';
import PlayArrowOutlinedIcon from '@mui/icons-material/PlayArrowOutlined';
import PauseOutlinedIcon from '@mui/icons-material/PauseOutlined';
import TaskAltOutlinedIcon from '@mui/icons-material/TaskAltOutlined';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import ReplayOutlinedIcon from '@mui/icons-material/ReplayOutlined';
import TuneOutlinedIcon from '@mui/icons-material/TuneOutlined';
import { incidentsApi } from '@/api/incidents';
import { referenceApi } from '@/api/reference';
import { ApiError } from '@/api/client';
import type {
  Impact,
  IncidentDetail,
  IncidentStatus,
  PendingReason,
  Priority,
  ResolutionCode,
  Urgency,
} from '@/api/types';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { useAuth } from '@/auth/useAuth';
import { humanise } from '@/utils/format';

const RESOLUTION_CODES: ResolutionCode[] = [
  'Resolved',
  'ResolvedByWorkaround',
  'ResolvedByKnownError',
  'ResolvedByChange',
  'Duplicate',
  'NoFaultFound',
  'UserEducated',
  'WithdrawnByRequester',
];

const PENDING_REASONS: PendingReason[] = [
  'AwaitingRequester',
  'AwaitingVendor',
  'AwaitingChange',
  'AwaitingParts',
  'AwaitingProblem',
];

const IMPACTS: Impact[] = ['Extensive', 'Significant', 'Moderate', 'Minor'];
const URGENCIES: Urgency[] = ['Critical', 'High', 'Medium', 'Low'];
const PRIORITIES: Priority[] = ['P1Critical', 'P2High', 'P3Moderate', 'P4Low', 'P5Planning'];

/**
 * The actions available on an incident.
 *
 * Buttons are driven by two things at once: what the state machine allows next
 * (`allowedTransitions`, which the server computes) and what the user is permitted to do. An
 * action that would be refused is not offered, and the server still checks both again.
 */
export function IncidentActionBar({
  incident,
  onChanged,
  onDeclareMajor: _onDeclareMajor,
}: {
  incident: IncidentDetail;
  onChanged: () => void;
  onDeclareMajor: (isMajor: boolean, reason: string) => void;
}) {
  const [dialog, setDialog] = useState<'assign' | 'resolve' | 'pending' | 'priority' | 'reopen' | null>(
    null,
  );

  const canTransitionTo = (status: IncidentStatus) => incident.allowedTransitions.includes(status);

  return (
    <>
      <Can permission={Permissions.incidentAssign}>
        <Button
          variant="outlined"
          size="small"
          startIcon={<PersonAddAltOutlinedIcon />}
          onClick={() => setDialog('assign')}
          disabled={incident.status === 'Closed' || incident.status === 'Cancelled'}
        >
          Assign
        </Button>
      </Can>

      <Can permission={Permissions.incidentUpdate}>
        <Button
          variant="outlined"
          size="small"
          startIcon={<TuneOutlinedIcon />}
          onClick={() => setDialog('priority')}
          disabled={incident.status === 'Closed' || incident.status === 'Cancelled'}
        >
          Reclassify
        </Button>
      </Can>

      {canTransitionTo('InProgress') && incident.status !== 'Resolved' && (
        <Can permission={Permissions.incidentUpdate}>
          <StatusButton
            incident={incident}
            status="InProgress"
            label="Start work"
            icon={<PlayArrowOutlinedIcon />}
            onChanged={onChanged}
          />
        </Can>
      )}

      {canTransitionTo('Pending') && (
        <Can permission={Permissions.incidentUpdate}>
          <Button
            variant="outlined"
            size="small"
            startIcon={<PauseOutlinedIcon />}
            onClick={() => setDialog('pending')}
          >
            Put on hold
          </Button>
        </Can>
      )}

      {canTransitionTo('Resolved') && (
        <Can permission={Permissions.incidentResolve}>
          <Button
            variant="contained"
            size="small"
            color="success"
            startIcon={<TaskAltOutlinedIcon />}
            onClick={() => setDialog('resolve')}
          >
            Resolve
          </Button>
        </Can>
      )}

      {canTransitionTo('Closed') && (
        <Can permission={Permissions.incidentClose}>
          <StatusButton
            incident={incident}
            status="Closed"
            label="Close"
            icon={<LockOutlinedIcon />}
            variant="contained"
            onChanged={onChanged}
          />
        </Can>
      )}

      {incident.status === 'Resolved' && (
        <Can permission={Permissions.incidentReopen}>
          <Button
            variant="outlined"
            size="small"
            color="warning"
            startIcon={<ReplayOutlinedIcon />}
            onClick={() => setDialog('reopen')}
          >
            Reopen
          </Button>
        </Can>
      )}

      <AssignDialog
        open={dialog === 'assign'}
        incident={incident}
        onClose={() => setDialog(null)}
        onChanged={onChanged}
      />

      <ResolveDialog
        open={dialog === 'resolve'}
        incident={incident}
        onClose={() => setDialog(null)}
        onChanged={onChanged}
      />

      <PendingDialog
        open={dialog === 'pending'}
        incident={incident}
        onClose={() => setDialog(null)}
        onChanged={onChanged}
      />

      <PriorityDialog
        open={dialog === 'priority'}
        incident={incident}
        onClose={() => setDialog(null)}
        onChanged={onChanged}
      />

      <ReopenDialog
        open={dialog === 'reopen'}
        incident={incident}
        onClose={() => setDialog(null)}
        onChanged={onChanged}
      />
    </>
  );
}

/** A status change with nothing more to collect than the button press. */
function StatusButton({
  incident,
  status,
  label,
  icon,
  variant = 'outlined',
  onChanged,
}: {
  incident: IncidentDetail;
  status: IncidentStatus;
  label: string;
  icon: React.ReactNode;
  variant?: 'outlined' | 'contained';
  onChanged: () => void;
}) {
  const mutation = useMutation({
    mutationFn: () => incidentsApi.changeStatus(incident.id, { status }),
    onSuccess: onChanged,
  });

  return (
    <Button
      variant={variant}
      size="small"
      startIcon={icon}
      disabled={mutation.isPending}
      onClick={() => mutation.mutate()}
    >
      {label}
    </Button>
  );
}

function AssignDialog({
  open,
  incident,
  onClose,
  onChanged,
}: {
  open: boolean;
  incident: IncidentDetail;
  onClose: () => void;
  onChanged: () => void;
}) {
  const [groupId, setGroupId] = useState(incident.assignmentGroupId ?? '');
  const [userId, setUserId] = useState(incident.assignedToUserId ?? '');
  const [note, setNote] = useState('');
  const [error, setError] = useState<string | null>(null);

  const { profile } = useAuth();
  const queryClient = useQueryClient();

  const groups = useQuery({
    queryKey: ['reference', 'groups'],
    queryFn: ({ signal }) => referenceApi.groups('Assignment', signal),
    enabled: open,
    staleTime: 5 * 60_000,
  });

  // Only members of the chosen group can be assigned, which is the rule the API enforces on
  // save. Offering anyone else here would produce a confusing rejection.
  const members = useQuery({
    queryKey: ['reference', 'group-members', groupId],
    queryFn: ({ signal }) => referenceApi.groupMembers(groupId, signal),
    enabled: open && Boolean(groupId),
  });

  const mutation = useMutation({
    mutationFn: () =>
      incidentsApi.assign(incident.id, {
        assignmentGroupId: groupId || null,
        assignedToUserId: userId || null,
        note: note.trim() || undefined,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['incidents'] });
      onChanged();
      onClose();
    },
    onError: (mutationError) =>
      setError(mutationError instanceof ApiError ? mutationError.userMessage : 'Assignment failed.'),
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Assign {incident.number}</DialogTitle>

      <DialogContent>
        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}

        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            select
            label="Assignment group"
            value={groupId}
            onChange={(event) => {
              setGroupId(event.target.value);
              // The current assignee may not belong to the new group.
              setUserId('');
            }}
            fullWidth
          >
            <MenuItem value="">Unassigned</MenuItem>
            {(groups.data ?? []).map((group) => (
              <MenuItem key={group.id} value={group.id}>
                {group.name}
              </MenuItem>
            ))}
          </TextField>

          <TextField
            select
            label="Assignee"
            value={userId}
            onChange={(event) => setUserId(event.target.value)}
            disabled={!groupId}
            helperText={groupId ? 'Only members of the selected group can be assigned.' : 'Choose a group first.'}
            fullWidth
          >
            <MenuItem value="">Leave in the group queue</MenuItem>
            {(members.data ?? []).map((member) => (
              <MenuItem key={member.id} value={member.id}>
                {member.displayName}
                {member.id === profile?.userId ? ' (me)' : ''}
              </MenuItem>
            ))}
          </TextField>

          <TextField
            label="Work note (optional)"
            value={note}
            onChange={(event) => setNote(event.target.value)}
            multiline
            minRows={2}
            fullWidth
            helperText="Recorded as an internal note alongside the assignment."
          />
        </Stack>
      </DialogContent>

      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={() => mutation.mutate()} disabled={mutation.isPending}>
          {mutation.isPending ? 'Assigning…' : 'Assign'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function ResolveDialog({
  open,
  incident,
  onClose,
  onChanged,
}: {
  open: boolean;
  incident: IncidentDetail;
  onClose: () => void;
  onChanged: () => void;
}) {
  const [code, setCode] = useState<ResolutionCode>('Resolved');
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () =>
      incidentsApi.changeStatus(incident.id, {
        status: 'Resolved',
        resolutionCode: code,
        notes: notes.trim(),
      }),
    onSuccess: () => {
      onChanged();
      onClose();
    },
    onError: (mutationError) =>
      setError(mutationError instanceof ApiError ? mutationError.userMessage : 'Could not resolve.'),
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Resolve {incident.number}</DialogTitle>

      <DialogContent>
        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}

        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          The requester is notified with these notes, so write them for the person who raised the
          incident rather than for the service desk.
        </Typography>

        <Stack spacing={2}>
          <TextField
            select
            label="Resolution code"
            value={code}
            onChange={(event) => setCode(event.target.value as ResolutionCode)}
            fullWidth
          >
            {RESOLUTION_CODES.map((option) => (
              <MenuItem key={option} value={option}>
                {humanise(option)}
              </MenuItem>
            ))}
          </TextField>

          <TextField
            label="Resolution notes"
            value={notes}
            onChange={(event) => setNotes(event.target.value)}
            multiline
            minRows={4}
            fullWidth
            required
            helperText={`${notes.trim().length}/10 characters minimum`}
          />
        </Stack>
      </DialogContent>

      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          color="success"
          onClick={() => mutation.mutate()}
          disabled={mutation.isPending || notes.trim().length < 10}
        >
          {mutation.isPending ? 'Resolving…' : 'Resolve'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function PendingDialog({
  open,
  incident,
  onClose,
  onChanged,
}: {
  open: boolean;
  incident: IncidentDetail;
  onClose: () => void;
  onChanged: () => void;
}) {
  const [reason, setReason] = useState<PendingReason>('AwaitingRequester');
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () =>
      incidentsApi.changeStatus(incident.id, {
        status: 'Pending',
        pendingReason: reason,
        notes: notes.trim() || undefined,
      }),
    onSuccess: () => {
      onChanged();
      onClose();
    },
    onError: (mutationError) =>
      setError(mutationError instanceof ApiError ? mutationError.userMessage : 'Could not put on hold.'),
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Put {incident.number} on hold</DialogTitle>

      <DialogContent>
        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}

        <Alert severity="info" sx={{ mb: 2 }}>
          The resolution SLA clock stops while an incident is on hold, and resumes when work
          restarts. The reason is recorded so the pause is explainable in reporting.
        </Alert>

        <Stack spacing={2}>
          <TextField
            select
            label="Waiting on"
            value={reason}
            onChange={(event) => setReason(event.target.value as PendingReason)}
            fullWidth
          >
            {PENDING_REASONS.map((option) => (
              <MenuItem key={option} value={option}>
                {humanise(option)}
              </MenuItem>
            ))}
          </TextField>

          <TextField
            label="Note (optional)"
            value={notes}
            onChange={(event) => setNotes(event.target.value)}
            multiline
            minRows={2}
            fullWidth
          />
        </Stack>
      </DialogContent>

      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={() => mutation.mutate()} disabled={mutation.isPending}>
          Put on hold
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function PriorityDialog({
  open,
  incident,
  onClose,
  onChanged,
}: {
  open: boolean;
  incident: IncidentDetail;
  onClose: () => void;
  onChanged: () => void;
}) {
  const { hasPermission } = useAuth();
  const canOverride = hasPermission(Permissions.incidentPriorityOverride);

  const [impact, setImpact] = useState<Impact>(incident.impact);
  const [urgency, setUrgency] = useState<Urgency>(incident.urgency);
  const [override, setOverride] = useState<Priority | ''>('');
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);

  const matrix = useQuery({
    queryKey: ['reference', 'priority-matrix'],
    queryFn: ({ signal }) => referenceApi.priorityMatrix(signal),
    enabled: open,
    staleTime: 10 * 60_000,
  });

  // Shows the derived priority as the agent changes impact and urgency, using the tenant's own
  // matrix rather than a rule duplicated in the browser.
  const derived = matrix.data?.find((row) => row.impact === impact && row.urgency === urgency)?.priority;

  const mutation = useMutation({
    mutationFn: () =>
      incidentsApi.changePriority(incident.id, {
        impact,
        urgency,
        overridePriority: override || undefined,
        overrideReason: override ? reason.trim() : undefined,
      }),
    onSuccess: () => {
      onChanged();
      onClose();
    },
    onError: (mutationError) =>
      setError(mutationError instanceof ApiError ? mutationError.userMessage : 'Could not reclassify.'),
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Reclassify {incident.number}</DialogTitle>

      <DialogContent>
        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}

        <Stack spacing={2} sx={{ mt: 1 }}>
          <Stack direction="row" spacing={2}>
            <TextField
              select
              label="Impact"
              value={impact}
              onChange={(event) => setImpact(event.target.value as Impact)}
              fullWidth
            >
              {IMPACTS.map((option) => (
                <MenuItem key={option} value={option}>
                  {humanise(option)}
                </MenuItem>
              ))}
            </TextField>

            <TextField
              select
              label="Urgency"
              value={urgency}
              onChange={(event) => setUrgency(event.target.value as Urgency)}
              fullWidth
            >
              {URGENCIES.map((option) => (
                <MenuItem key={option} value={option}>
                  {humanise(option)}
                </MenuItem>
              ))}
            </TextField>
          </Stack>

          {derived && (
            <Alert severity="info">
              This combination gives <strong>{derived}</strong> on your organization's priority
              matrix.
            </Alert>
          )}

          {canOverride && (
            <>
              <TextField
                select
                label="Override priority (optional)"
                value={override}
                onChange={(event) => setOverride(event.target.value as Priority | '')}
                fullWidth
                helperText="Departing from the matrix is recorded in the audit trail."
              >
                <MenuItem value="">Use the matrix</MenuItem>
                {PRIORITIES.map((option) => (
                  <MenuItem key={option} value={option}>
                    {option}
                  </MenuItem>
                ))}
              </TextField>

              {override && (
                <TextField
                  label="Reason for the override"
                  value={reason}
                  onChange={(event) => setReason(event.target.value)}
                  multiline
                  minRows={2}
                  fullWidth
                  required
                  helperText={`${reason.trim().length}/10 characters minimum`}
                />
              )}
            </>
          )}
        </Stack>
      </DialogContent>

      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          onClick={() => mutation.mutate()}
          disabled={mutation.isPending || (Boolean(override) && reason.trim().length < 10)}
        >
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function ReopenDialog({
  open,
  incident,
  onClose,
  onChanged,
}: {
  open: boolean;
  incident: IncidentDetail;
  onClose: () => void;
  onChanged: () => void;
}) {
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () =>
      incidentsApi.changeStatus(incident.id, { status: 'InProgress', notes: notes.trim() }),
    onSuccess: () => {
      onChanged();
      onClose();
    },
    onError: (mutationError) =>
      setError(mutationError instanceof ApiError ? mutationError.userMessage : 'Could not reopen.'),
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Reopen {incident.number}</DialogTitle>

      <DialogContent>
        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}

        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Reopening clears the resolution and restarts the resolution SLA. The reopen count is
          reported, so this is visible in service quality reviews.
        </Typography>

        <TextField
          label="Why is this being reopened?"
          value={notes}
          onChange={(event) => setNotes(event.target.value)}
          multiline
          minRows={3}
          fullWidth
          required
        />
      </DialogContent>

      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          color="warning"
          onClick={() => mutation.mutate()}
          disabled={mutation.isPending || notes.trim().length === 0}
        >
          Reopen
        </Button>
      </DialogActions>
    </Dialog>
  );
}
