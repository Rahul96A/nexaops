import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Box,
  Button,
  Card,
  Checkbox,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  LinearProgress,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import { adminApi } from '@/api/admin';
import { ApiError } from '@/api/client';
import type { PermissionInfo, RoleDetail } from '@/api/types';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { ErrorState } from '@/components/ErrorState';

/** Roles and exactly what they grant. */
export function RoleSettings() {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<RoleDetail | null>(null);
  const [creating, setCreating] = useState(false);

  const roles = useQuery({
    queryKey: ['admin-roles'],
    queryFn: ({ signal }) => adminApi.roles(signal),
  });

  const remove = useMutation({
    mutationFn: (id: string) => adminApi.deleteRole(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin-roles'] }),
  });

  return (
    <Box>
      <Stack direction="row" sx={{ mb: 2 }} justifyContent="flex-end">
        <Can permission={Permissions.roleManage}>
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
            New role
          </Button>
        </Can>
      </Stack>

      {remove.error instanceof ApiError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {remove.error.userMessage}
        </Alert>
      )}

      {roles.isLoading && <LinearProgress />}
      {roles.error && <ErrorState error={roles.error} onRetry={() => void roles.refetch()} />}

      {roles.data && (
        <Card variant="outlined">
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Role</TableCell>
                  <TableCell align="right">Permissions</TableCell>
                  <TableCell align="right">People</TableCell>
                  <TableCell />
                </TableRow>
              </TableHead>
              <TableBody>
                {roles.data.map((role) => (
                  <TableRow key={role.id} hover>
                    <TableCell>
                      <Stack direction="row" gap={1} alignItems="center">
                        <Typography variant="body2" fontWeight={600}>
                          {role.name}
                        </Typography>

                        {role.isSystem && (
                          <Tooltip title="Provisioned with every tenant. It cannot be edited or deleted, because the next provisioning run would put it back as it was.">
                            <Chip
                              icon={<LockOutlinedIcon />}
                              label="Built in"
                              size="small"
                              variant="outlined"
                            />
                          </Tooltip>
                        )}
                      </Stack>

                      {role.description && (
                        <Typography variant="caption" color="text.secondary">
                          {role.description}
                        </Typography>
                      )}
                    </TableCell>
                    <TableCell align="right">{role.permissions.length}</TableCell>
                    <TableCell align="right">{role.userCount}</TableCell>
                    <TableCell align="right">
                      <Stack direction="row" gap={1} justifyContent="flex-end">
                        <Button size="small" onClick={() => setEditing(role)}>
                          {role.isSystem ? 'View' : 'Edit'}
                        </Button>

                        <Can permission={Permissions.roleManage}>
                          <Button
                            size="small"
                            color="error"
                            disabled={role.isSystem || role.userCount > 0 || remove.isPending}
                            onClick={() => remove.mutate(role.id)}
                          >
                            Delete
                          </Button>
                        </Can>
                      </Stack>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        </Card>
      )}

      {(editing || creating) && (
        <RoleDialog
          role={editing}
          onClose={() => {
            setEditing(null);
            setCreating(false);
          }}
          onSaved={() => {
            setEditing(null);
            setCreating(false);
            void queryClient.invalidateQueries({ queryKey: ['admin-roles'] });
          }}
        />
      )}
    </Box>
  );
}

function RoleDialog({
  role,
  onClose,
  onSaved,
}: {
  role: RoleDetail | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const readOnly = role?.isSystem ?? false;

  const [name, setName] = useState(role?.name ?? '');
  const [description, setDescription] = useState(role?.description ?? '');
  const [granted, setGranted] = useState<string[]>(role?.permissions ?? []);

  const permissions = useQuery({
    queryKey: ['admin-permissions'],
    queryFn: ({ signal }) => adminApi.permissions(signal),
  });

  const catalogue = permissions.data;

  const grouped = useMemo(() => {
    const map = new Map<string, PermissionInfo[]>();

    for (const permission of catalogue ?? []) {
      map.set(permission.category, [...(map.get(permission.category) ?? []), permission]);
    }

    return [...map.entries()];
  }, [catalogue]);

  const save = useMutation({
    mutationFn: () => {
      const input = {
        name,
        description: description || null,
        permissions: granted,
        rowVersion: role?.rowVersion ?? null,
      };

      return role ? adminApi.updateRole(role.id, input) : adminApi.createRole(input);
    },
    onSuccess: onSaved,
  });

  function toggle(code: string) {
    setGranted((current) =>
      current.includes(code) ? current.filter((c) => c !== code) : [...current, code],
    );
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>{role ? role.name : 'New role'}</DialogTitle>
      <DialogContent>
        <Stack gap={2} sx={{ pt: 1 }}>
          {readOnly && (
            <Alert severity="info">
              This role is provisioned with every tenant, so it cannot be changed here — an edit
              would be put back on the next provisioning run. Create a role of your own if you
              need a different set of permissions.
            </Alert>
          )}

          {save.error instanceof ApiError && (
            <Alert severity="error">{save.error.userMessage}</Alert>
          )}

          <TextField
            label="Name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            disabled={readOnly}
            required
          />

          <TextField
            label="Description"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            disabled={readOnly}
            multiline
            minRows={2}
          />

          <Typography variant="subtitle2">
            Grants {granted.length} permission{granted.length === 1 ? '' : 's'}
          </Typography>

          {permissions.isLoading && <LinearProgress />}

          {grouped.map(([category, items]) => (
            <Accordion key={category} disableGutters variant="outlined">
              <AccordionSummary expandIcon={<ExpandMoreIcon />}>
                <Stack direction="row" gap={1} alignItems="center">
                  <Typography variant="body2" fontWeight={600}>
                    {category}
                  </Typography>
                  <Chip
                    size="small"
                    label={`${items.filter((p) => granted.includes(p.code)).length}/${items.length}`}
                  />
                </Stack>
              </AccordionSummary>
              <AccordionDetails>
                <Stack>
                  {items.map((permission) => (
                    <FormControlLabel
                      key={permission.code}
                      control={
                        <Checkbox
                          size="small"
                          checked={granted.includes(permission.code)}
                          disabled={readOnly}
                          onChange={() => toggle(permission.code)}
                        />
                      }
                      label={
                        <Box>
                          <Typography variant="body2">{permission.name}</Typography>
                          <Typography variant="caption" color="text.secondary">
                            {permission.description}
                          </Typography>
                        </Box>
                      }
                      sx={{ alignItems: 'flex-start', mb: 0.5 }}
                    />
                  ))}
                </Stack>
              </AccordionDetails>
            </Accordion>
          ))}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{readOnly ? 'Close' : 'Cancel'}</Button>

        {!readOnly && (
          <Button
            variant="contained"
            disabled={!name.trim() || save.isPending}
            onClick={() => save.mutate()}
          >
            Save
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
}
