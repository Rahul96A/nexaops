import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Autocomplete,
  Avatar,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  LinearProgress,
  MenuItem,
  Stack,
  Switch,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import PersonRemoveOutlinedIcon from '@mui/icons-material/PersonRemoveOutlined';
import { adminApi } from '@/api/admin';
import { referenceApi } from '@/api/reference';
import { ApiError } from '@/api/client';
import type { GroupAdmin, GroupType, UserSummary } from '@/api/types';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { ErrorState } from '@/components/ErrorState';
import { initials } from '@/utils/format';

const types: { value: GroupType; label: string; help: string }[] = [
  { value: 'Assignment', label: 'Assignment', help: 'Owns a queue of work.' },
  { value: 'Approval', label: 'Approval', help: 'Gates change and request approvals.' },
  { value: 'Notification', label: 'Notification', help: 'A broadcast target only.' },
  { value: 'Security', label: 'Security', help: 'Used for access decisions.' },
];

/** Teams, and who is in them. */
export function GroupSettings() {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<GroupAdmin | null>(null);
  const [creating, setCreating] = useState(false);

  const groups = useQuery({
    queryKey: ['admin-groups'],
    queryFn: ({ signal }) => adminApi.groups(signal),
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin-groups'] });

  const setMember = useMutation({
    mutationFn: ({ groupId, userId, isLead }: { groupId: string; userId: string; isLead: boolean }) =>
      adminApi.setGroupMember(groupId, userId, isLead),
    onSuccess: () => void invalidate(),
  });

  const removeMember = useMutation({
    mutationFn: ({ groupId, userId }: { groupId: string; userId: string }) =>
      adminApi.removeGroupMember(groupId, userId),
    onSuccess: () => void invalidate(),
  });

  return (
    <Box>
      <Stack direction="row" sx={{ mb: 2 }} justifyContent="flex-end">
        <Can permission={Permissions.groupManage}>
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
            New group
          </Button>
        </Can>
      </Stack>

      {(setMember.error ?? removeMember.error) instanceof ApiError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {((setMember.error ?? removeMember.error) as ApiError).userMessage}
        </Alert>
      )}

      {groups.isLoading && <LinearProgress />}
      {groups.error && <ErrorState error={groups.error} onRetry={() => void groups.refetch()} />}

      <Stack gap={1}>
        {(groups.data ?? []).map((group) => (
          <Accordion key={group.id} disableGutters variant="outlined">
            <AccordionSummary expandIcon={<ExpandMoreIcon />}>
              <Stack
                direction={{ xs: 'column', sm: 'row' }}
                gap={1.5}
                alignItems={{ xs: 'flex-start', sm: 'center' }}
                sx={{ width: '100%', pr: 2 }}
              >
                <Typography variant="body2" fontWeight={600} sx={{ flex: 1 }}>
                  {group.name}
                </Typography>

                <Chip label={group.type} size="small" variant="outlined" />

                {!group.isActive && <Chip label="Inactive" size="small" color="warning" />}

                <Typography variant="caption" color="text.secondary">
                  {group.memberCount} member{group.memberCount === 1 ? '' : 's'}
                </Typography>
              </Stack>
            </AccordionSummary>

            <AccordionDetails>
              <Stack gap={2}>
                <Stack direction="row" gap={2} flexWrap="wrap">
                  <Field label="Manager" value={group.managerName} />
                  <Field label="Default assignee" value={group.defaultAssigneeName} />
                  <Field label="Calendar" value={group.businessCalendarName} />
                  <Field label="Mailbox" value={group.email} />
                </Stack>

                <Stack gap={0.5}>
                  {group.members.map((member) => (
                    <Stack key={member.userId} direction="row" gap={1.5} alignItems="center">
                      <Avatar
                        sx={{
                          width: 26,
                          height: 26,
                          fontSize: 11,
                          bgcolor: member.avatarColor ?? 'primary.main',
                          opacity: member.isActive ? 1 : 0.4,
                        }}
                      >
                        {initials(member.displayName)}
                      </Avatar>

                      <Box sx={{ flex: 1, minWidth: 0 }}>
                        <Typography variant="body2">{member.displayName}</Typography>
                        <Typography variant="caption" color="text.secondary">
                          {member.jobTitle ?? member.email}
                        </Typography>
                      </Box>

                      {!member.isActive && (
                        <Tooltip title="This account is disabled. They stay listed here so the gap is visible rather than silent.">
                          <Chip label="Inactive" size="small" color="warning" variant="outlined" />
                        </Tooltip>
                      )}

                      <Can permission={Permissions.groupManage}>
                        <Tooltip title="Leads can reassign work inside the group">
                          <Switch
                            size="small"
                            checked={member.isLead}
                            onChange={(event) =>
                              setMember.mutate({
                                groupId: group.id,
                                userId: member.userId,
                                isLead: event.target.checked,
                              })
                            }
                          />
                        </Tooltip>

                        <IconButton
                          size="small"
                          aria-label={`Remove ${member.displayName}`}
                          onClick={() =>
                            removeMember.mutate({ groupId: group.id, userId: member.userId })
                          }
                        >
                          <PersonRemoveOutlinedIcon fontSize="small" />
                        </IconButton>
                      </Can>
                    </Stack>
                  ))}

                  {group.members.length === 0 && (
                    <Typography variant="body2" color="text.secondary">
                      Nobody is in this group. Work routed here would sit unowned.
                    </Typography>
                  )}
                </Stack>

                <Can permission={Permissions.groupManage}>
                  <Stack direction="row" gap={1}>
                    <AddMember
                      onAdd={(userId) =>
                        setMember.mutate({ groupId: group.id, userId, isLead: false })
                      }
                    />
                    <Button size="small" onClick={() => setEditing(group)}>
                      Edit group
                    </Button>
                  </Stack>
                </Can>
              </Stack>
            </AccordionDetails>
          </Accordion>
        ))}
      </Stack>

      {(editing || creating) && (
        <GroupDialog
          group={editing}
          onClose={() => {
            setEditing(null);
            setCreating(false);
          }}
          onSaved={() => {
            setEditing(null);
            setCreating(false);
            void invalidate();
          }}
        />
      )}
    </Box>
  );
}

function AddMember({ onAdd }: { onAdd: (userId: string) => void }) {
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<UserSummary | null>(null);

  const users = useQuery({
    queryKey: ['user-search', search],
    queryFn: ({ signal }) => referenceApi.searchUsers(search, signal),
    enabled: search.trim().length >= 2,
  });

  return (
    <Stack direction="row" gap={1} alignItems="center">
      <Autocomplete
        size="small"
        options={users.data ?? []}
        value={selected}
        onChange={(_, value) => setSelected(value)}
        onInputChange={(_, value) => setSearch(value)}
        getOptionLabel={(option) => option.displayName}
        isOptionEqualToValue={(option, value) => option.id === value.id}
        noOptionsText={search.trim().length < 2 ? 'Type at least two characters' : 'Nobody matches'}
        sx={{ minWidth: 240 }}
        renderInput={(params) => <TextField {...params} label="Add someone" />}
      />

      <Button
        size="small"
        disabled={!selected}
        onClick={() => {
          if (selected) {
            onAdd(selected.id);
            setSelected(null);
          }
        }}
      >
        Add
      </Button>
    </Stack>
  );
}

function GroupDialog({
  group,
  onClose,
  onSaved,
}: {
  group: GroupAdmin | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState(group?.name ?? '');
  const [description, setDescription] = useState(group?.description ?? '');
  const [type, setType] = useState<GroupType>(group?.type ?? 'Assignment');
  const [email, setEmail] = useState(group?.email ?? '');
  const [isActive, setIsActive] = useState(group?.isActive ?? true);

  const save = useMutation({
    mutationFn: () => {
      const input = {
        name,
        description: description || null,
        type,
        email: email || null,
        managerUserId: group?.managerUserId ?? null,
        defaultAssigneeUserId: group?.defaultAssigneeUserId ?? null,
        isActive,
        rowVersion: group?.rowVersion ?? null,
      };

      return group ? adminApi.updateGroup(group.id, input) : adminApi.createGroup(input);
    },
    onSuccess: onSaved,
  });

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{group ? group.name : 'New group'}</DialogTitle>
      <DialogContent>
        <Stack gap={2} sx={{ pt: 1 }}>
          {save.error instanceof ApiError && (
            <Alert severity="error">{save.error.userMessage}</Alert>
          )}

          <TextField
            label="Name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            required
          />

          <TextField
            select
            label="Type"
            value={type}
            onChange={(event) => setType(event.target.value as GroupType)}
            helperText={types.find((t) => t.value === type)?.help}
          >
            {types.map((option) => (
              <MenuItem key={option.value} value={option.value}>
                {option.label}
              </MenuItem>
            ))}
          </TextField>

          <TextField
            label="Description"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            multiline
            minRows={2}
          />

          <TextField
            label="Shared mailbox"
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            helperText="Optional. Used by email notification rules."
          />

          <Stack direction="row" gap={1} alignItems="center">
            <Switch checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />
            <Typography variant="body2">Active</Typography>
          </Stack>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={!name.trim() || save.isPending}
          onClick={() => save.mutate()}
        >
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function Field({ label, value }: { label: string; value?: string | null }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary" display="block">
        {label}
      </Typography>
      <Typography variant="body2">{value ?? '—'}</Typography>
    </Box>
  );
}
