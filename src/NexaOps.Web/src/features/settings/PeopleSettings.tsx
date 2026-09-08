import { useState } from 'react';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Avatar,
  Box,
  Button,
  Card,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  InputAdornment,
  LinearProgress,
  MenuItem,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TablePagination,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import SearchIcon from '@mui/icons-material/Search';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import BadgeOutlinedIcon from '@mui/icons-material/BadgeOutlined';
import { adminApi } from '@/api/admin';
import { ApiError } from '@/api/client';
import type { CreatedUser, UserListItem, UserStatus } from '@/api/types';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { ErrorState } from '@/components/ErrorState';
import { formatRelative, initials } from '@/utils/format';

const statusColours: Record<UserStatus, 'success' | 'default' | 'warning' | 'error'> = {
  Active: 'success',
  Disabled: 'default',
  Suspended: 'warning',
  Locked: 'error',
};

/** The tenant directory: who exists, what they hold, and whether they can sign in. */
export function PeopleSettings() {
  const queryClient = useQueryClient();

  const [search, setSearch] = useState('');
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(25);
  const [selected, setSelected] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [created, setCreated] = useState<CreatedUser | null>(null);

  const users = useQuery({
    queryKey: ['admin-users', search, page, pageSize],
    queryFn: ({ signal }) =>
      adminApi.users({ search: search || undefined, page: page + 1, pageSize }, signal),
    placeholderData: keepPreviousData,
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['admin-users'] });
    void queryClient.invalidateQueries({ queryKey: ['admin-user'] });
  };

  const setStatus = useMutation({
    mutationFn: ({ id, status }: { id: string; status: UserStatus }) =>
      adminApi.setUserStatus(id, status),
    onSuccess: invalidate,
  });

  return (
    <Box>
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2} sx={{ mb: 2 }} alignItems="center">
        <TextField
          size="small"
          placeholder="Search by name, email or employee number"
          defaultValue={search}
          onBlur={(event) => {
            setSearch(event.target.value);
            setPage(0);
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              setSearch((event.target as HTMLInputElement).value);
              setPage(0);
            }
          }}
          sx={{ minWidth: 360 }}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            },
          }}
        />

        <Box sx={{ flex: 1 }} />

        <Can permission={Permissions.userManage}>
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
            Add person
          </Button>
        </Can>
      </Stack>

      {setStatus.error instanceof ApiError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {setStatus.error.userMessage}
        </Alert>
      )}

      {(users.isLoading || users.isFetching) && <LinearProgress />}
      {users.error && <ErrorState error={users.error} onRetry={() => void users.refetch()} />}

      {users.data && (
        <Card variant="outlined">
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Person</TableCell>
                  <TableCell>Job title</TableCell>
                  <TableCell>Department</TableCell>
                  <TableCell align="right">Roles</TableCell>
                  <TableCell>Last signed in</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell />
                </TableRow>
              </TableHead>
              <TableBody>
                {users.data.items.map((user) => (
                  <TableRow key={user.id} hover>
                    <TableCell>
                      <Stack direction="row" gap={1.5} alignItems="center">
                        <Avatar
                          sx={{
                            width: 30,
                            height: 30,
                            fontSize: 12,
                            bgcolor: user.avatarColor ?? 'primary.main',
                          }}
                        >
                          {initials(user.displayName)}
                        </Avatar>
                        <Box>
                          <Typography variant="body2" fontWeight={600}>
                            {user.displayName}
                          </Typography>
                          <Typography variant="caption" color="text.secondary">
                            {user.email}
                          </Typography>
                        </Box>
                      </Stack>
                    </TableCell>
                    <TableCell>{user.jobTitle ?? '—'}</TableCell>
                    <TableCell>{user.departmentName ?? '—'}</TableCell>
                    <TableCell align="right">{user.roleCount}</TableCell>
                    <TableCell>
                      <Typography variant="body2" color="text.secondary">
                        {user.lastLoginAt ? formatRelative(user.lastLoginAt) : 'Never'}
                      </Typography>
                    </TableCell>
                    <TableCell>
                      <Chip
                        label={user.status}
                        size="small"
                        color={statusColours[user.status]}
                        variant={user.status === 'Active' ? 'filled' : 'outlined'}
                      />
                    </TableCell>
                    <TableCell align="right">
                      <Stack direction="row" gap={0.5} justifyContent="flex-end">
                        <Can permission={Permissions.roleManage}>
                          <Tooltip title="Roles">
                            <IconButton size="small" onClick={() => setSelected(user.id)}>
                              <BadgeOutlinedIcon fontSize="small" />
                            </IconButton>
                          </Tooltip>
                        </Can>

                        <Can permission={Permissions.userManage}>
                          <Button
                            size="small"
                            disabled={setStatus.isPending}
                            onClick={() =>
                              setStatus.mutate({
                                id: user.id,
                                status: user.status === 'Active' ? 'Disabled' : 'Active',
                              })
                            }
                          >
                            {user.status === 'Active' ? 'Disable' : 'Enable'}
                          </Button>
                        </Can>
                      </Stack>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>

          <TablePagination
            component="div"
            count={users.data.totalCount}
            page={page}
            rowsPerPage={pageSize}
            rowsPerPageOptions={[10, 25, 50, 100]}
            onPageChange={(_, next) => setPage(next)}
            onRowsPerPageChange={(event) => {
              setPageSize(Number(event.target.value));
              setPage(0);
            }}
          />
        </Card>
      )}

      {selected && (
        <RolesDialog
          userId={selected}
          onClose={() => setSelected(null)}
          onSaved={() => {
            setSelected(null);
            invalidate();
          }}
        />
      )}

      {creating && (
        <NewPersonDialog
          onClose={() => setCreating(false)}
          onCreated={(result) => {
            setCreating(false);
            setCreated(result);
            invalidate();
          }}
        />
      )}

      {created && <TemporaryPasswordDialog created={created} onClose={() => setCreated(null)} />}
    </Box>
  );
}

function RolesDialog({
  userId,
  onClose,
  onSaved,
}: {
  userId: string;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [chosen, setChosen] = useState<string[] | null>(null);

  const user = useQuery({
    queryKey: ['admin-user', userId],
    queryFn: ({ signal }) => adminApi.user(userId, signal),
  });

  const roles = useQuery({
    queryKey: ['admin-roles'],
    queryFn: ({ signal }) => adminApi.roles(signal),
  });

  const save = useMutation({
    mutationFn: () => adminApi.setUserRoles(userId, chosen ?? []),
    onSuccess: onSaved,
  });

  const current = chosen ?? user.data?.roles.map((r) => r.id) ?? [];

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Roles for {user.data?.displayName ?? '…'}</DialogTitle>
      <DialogContent>
        {(user.isLoading || roles.isLoading) && <LinearProgress />}

        {save.error instanceof ApiError && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {save.error.userMessage}
          </Alert>
        )}

        <Alert severity="info" sx={{ mb: 2 }}>
          Changing somebody&rsquo;s roles ends their current sessions straight away. They will be
          asked to sign in again, and will hold exactly what these roles grant.
        </Alert>

        <TextField
          select
          fullWidth
          label="Roles"
          value={current}
          onChange={(event) => {
            const value = event.target.value;
            setChosen(typeof value === 'string' ? value.split(',') : (value as unknown as string[]));
          }}
          slotProps={{ select: { multiple: true } }}
        >
          {(roles.data ?? []).map((role) => (
            <MenuItem key={role.id} value={role.id}>
              {role.name}
              {role.isSystem ? ' (built in)' : ''}
            </MenuItem>
          ))}
        </TextField>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={save.isPending} onClick={() => save.mutate()}>
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function NewPersonDialog({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (created: CreatedUser) => void;
}) {
  const [email, setEmail] = useState('');
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [jobTitle, setJobTitle] = useState('');

  const create = useMutation({
    mutationFn: () =>
      adminApi.createUser({ email, firstName, lastName, jobTitle: jobTitle || null }),
    onSuccess: onCreated,
  });

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Add a person</DialogTitle>
      <DialogContent>
        <Stack gap={2} sx={{ pt: 1 }}>
          {create.error instanceof ApiError && (
            <Alert severity="error">{create.error.userMessage}</Alert>
          )}

          <TextField
            label="Email"
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            helperText="This is the sign-in address."
            required
          />

          <Stack direction="row" gap={2}>
            <TextField
              label="First name"
              value={firstName}
              onChange={(event) => setFirstName(event.target.value)}
              sx={{ flex: 1 }}
              required
            />
            <TextField
              label="Last name"
              value={lastName}
              onChange={(event) => setLastName(event.target.value)}
              sx={{ flex: 1 }}
              required
            />
          </Stack>

          <TextField
            label="Job title"
            value={jobTitle}
            onChange={(event) => setJobTitle(event.target.value)}
          />

          <Alert severity="info">
            A new account holds no roles until you give it some, and starts with a one-time
            password shown once on the next screen.
          </Alert>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={!email || !firstName || !lastName || create.isPending}
          onClick={() => create.mutate()}
        >
          Create
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/**
 * Shows the generated password once.
 *
 * Once, and only here. The server does not store it in the clear and no endpoint returns it
 * again, so the dialog says so plainly rather than letting somebody assume they can come back
 * for it later.
 */
function TemporaryPasswordDialog({
  created,
  onClose,
}: {
  created: CreatedUser;
  onClose: () => void;
}) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    if (!created.temporaryPassword) {
      return;
    }

    try {
      await navigator.clipboard.writeText(created.temporaryPassword);
      setCopied(true);
    } catch {
      // Clipboard access can be refused outright. The password is on screen either way.
      setCopied(false);
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{created.user.displayName} has been added</DialogTitle>
      <DialogContent>
        {created.temporaryPassword ? (
          <Stack gap={2}>
            <Alert severity="warning">
              This password is shown once. It is not stored anywhere it can be read back, so if
              you close this dialog without noting it, the account will need a new one.
            </Alert>

            <Stack direction="row" gap={1} alignItems="center">
              <TextField
                label="One-time password"
                value={created.temporaryPassword}
                slotProps={{ input: { readOnly: true } }}
                sx={{ flex: 1, '& input': { fontFamily: 'monospace' } }}
              />
              <Tooltip title={copied ? 'Copied' : 'Copy'}>
                <IconButton onClick={() => void copy()} aria-label="Copy password">
                  <ContentCopyIcon />
                </IconButton>
              </Tooltip>
            </Stack>

            <Typography variant="body2" color="text.secondary">
              They will be asked to change it when they first sign in.
            </Typography>
          </Stack>
        ) : (
          <Alert severity="info">
            This tenant authenticates through an external identity provider, so NexaOps has not
            issued a password. The account will be matched to their directory identity when they
            first sign in.
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button variant="contained" onClick={onClose}>
          Done
        </Button>
      </DialogActions>
    </Dialog>
  );
}

export type { UserListItem };
