import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  LinearProgress,
  MenuItem,
  Stack,
  Switch,
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
import { adminApi } from '@/api/admin';
import { referenceApi } from '@/api/reference';
import { ApiError } from '@/api/client';
import type { CategoryAdmin, ServiceModule } from '@/api/types';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { ErrorState } from '@/components/ErrorState';

/** The modules that classify. Others do not use the taxonomy. */
const modules: { value: ServiceModule; label: string }[] = [
  { value: 'Incident', label: 'Incidents' },
  { value: 'Request', label: 'Service requests' },
  { value: 'Problem', label: 'Problems' },
  { value: 'Change', label: 'Changes' },
];

/** How work is classified, per module. */
export function CategorySettings() {
  const queryClient = useQueryClient();

  const [module, setModule] = useState<ServiceModule>('Incident');
  const [editing, setEditing] = useState<CategoryAdmin | null>(null);
  const [creating, setCreating] = useState(false);

  const categories = useQuery({
    queryKey: ['admin-categories', module],
    queryFn: ({ signal }) => adminApi.categories(module, signal),
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin-categories'] });

  const remove = useMutation({
    mutationFn: (id: string) => adminApi.deleteCategory(id),
    onSuccess: () => void invalidate(),
  });

  const setActive = useMutation({
    mutationFn: (category: CategoryAdmin) =>
      adminApi.updateCategory(category.id, {
        name: category.name,
        description: category.description ?? null,
        module: category.module,
        defaultAssignmentGroupId: category.defaultAssignmentGroupId ?? null,
        sortOrder: category.sortOrder,
        isActive: !category.isActive,
        rowVersion: category.rowVersion ?? null,
      }),
    onSuccess: () => void invalidate(),
  });

  const error = (remove.error ?? setActive.error) as ApiError | null;

  return (
    <Box>
      <Stack direction="row" gap={2} sx={{ mb: 2 }} alignItems="center">
        <TextField
          select
          size="small"
          label="Module"
          value={module}
          onChange={(event) => setModule(event.target.value as ServiceModule)}
          sx={{ minWidth: 200 }}
        >
          {modules.map((option) => (
            <MenuItem key={option.value} value={option.value}>
              {option.label}
            </MenuItem>
          ))}
        </TextField>

        <Box sx={{ flex: 1 }} />

        <Can permission={Permissions.categoryManage}>
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
            New category
          </Button>
        </Can>
      </Stack>

      {error instanceof ApiError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error.userMessage}
        </Alert>
      )}

      {categories.isLoading && <LinearProgress />}
      {categories.error && (
        <ErrorState error={categories.error} onRetry={() => void categories.refetch()} />
      )}

      {categories.data && (
        <Card variant="outlined">
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Category</TableCell>
                  <TableCell>Code</TableCell>
                  <TableCell>Routes to</TableCell>
                  <TableCell align="right">Subcategories</TableCell>
                  <TableCell align="right">In use by</TableCell>
                  <TableCell>Active</TableCell>
                  <TableCell />
                </TableRow>
              </TableHead>
              <TableBody>
                {categories.data.map((category) => (
                  <TableRow key={category.id} hover>
                    <TableCell>
                      <Typography variant="body2" fontWeight={600}>
                        {category.name}
                      </Typography>
                      {category.description && (
                        <Typography variant="caption" color="text.secondary">
                          {category.description}
                        </Typography>
                      )}
                    </TableCell>
                    <TableCell>
                      <Typography variant="body2" fontFamily="monospace">
                        {category.code}
                      </Typography>
                    </TableCell>
                    <TableCell>{category.defaultAssignmentGroupName ?? '—'}</TableCell>
                    <TableCell align="right">{category.subcategories.length}</TableCell>
                    <TableCell align="right">
                      <Tooltip
                        title={
                          category.recordCount > 0
                            ? 'Records classify here, so it cannot be deleted. Deactivating hides it from new records and leaves the history intact.'
                            : 'Nothing classifies here.'
                        }
                      >
                        <span>{category.recordCount}</span>
                      </Tooltip>
                    </TableCell>
                    <TableCell>
                      <Can permission={Permissions.categoryManage}>
                        <Switch
                          size="small"
                          checked={category.isActive}
                          disabled={setActive.isPending}
                          onChange={() => setActive.mutate(category)}
                          inputProps={{ 'aria-label': `Activate ${category.name}` }}
                        />
                      </Can>
                    </TableCell>
                    <TableCell align="right">
                      <Stack direction="row" gap={1} justifyContent="flex-end">
                        <Can permission={Permissions.categoryManage}>
                          <Button size="small" onClick={() => setEditing(category)}>
                            Edit
                          </Button>

                          <Tooltip
                            title={
                              category.recordCount > 0
                                ? 'Records classify here. Deactivate it instead.'
                                : ''
                            }
                          >
                            <span>
                              <Button
                                size="small"
                                color="error"
                                disabled={category.recordCount > 0 || remove.isPending}
                                onClick={() => remove.mutate(category.id)}
                              >
                                Delete
                              </Button>
                            </span>
                          </Tooltip>
                        </Can>
                      </Stack>
                    </TableCell>
                  </TableRow>
                ))}

                {categories.data.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={7}>
                      <Typography variant="body2" color="text.secondary">
                        No categories for this module yet.
                      </Typography>
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </TableContainer>
        </Card>
      )}

      {(editing || creating) && (
        <CategoryDialog
          category={editing}
          module={module}
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

function CategoryDialog({
  category,
  module,
  onClose,
  onSaved,
}: {
  category: CategoryAdmin | null;
  module: ServiceModule;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState(category?.name ?? '');
  const [description, setDescription] = useState(category?.description ?? '');
  const [groupId, setGroupId] = useState(category?.defaultAssignmentGroupId ?? '');
  const [sortOrder, setSortOrder] = useState(category?.sortOrder ?? 0);
  const [isActive, setIsActive] = useState(category?.isActive ?? true);

  const groups = useQuery({
    queryKey: ['groups', 'Assignment'],
    queryFn: ({ signal }) => referenceApi.groups('Assignment', signal),
  });

  const save = useMutation({
    mutationFn: () => {
      const input = {
        name,
        description: description || null,
        module: category?.module ?? module,
        defaultAssignmentGroupId: groupId || null,
        sortOrder,
        isActive,
        rowVersion: category?.rowVersion ?? null,
      };

      return category ? adminApi.updateCategory(category.id, input) : adminApi.createCategory(input);
    },
    onSuccess: onSaved,
  });

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{category ? category.name : 'New category'}</DialogTitle>
      <DialogContent>
        <Stack gap={2} sx={{ pt: 1 }}>
          {save.error instanceof ApiError && (
            <Alert severity="error">{save.error.userMessage}</Alert>
          )}

          {category && (
            <Alert severity="info">
              A category belongs to {category.module} and cannot be moved to another module —
              records already classified here would be left under a taxonomy that no longer claims
              them.
            </Alert>
          )}

          <TextField
            label="Name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            required
          />

          <TextField
            label="Description"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            multiline
            minRows={2}
          />

          <TextField
            select
            label="Route to"
            value={groupId}
            onChange={(event) => setGroupId(event.target.value)}
            helperText="Where records classified here go when nothing more specific matches."
          >
            <MenuItem value="">Nowhere in particular</MenuItem>
            {(groups.data ?? []).map((group) => (
              <MenuItem key={group.id} value={group.id}>
                {group.name}
              </MenuItem>
            ))}
          </TextField>

          <Stack direction="row" gap={2} alignItems="center">
            <TextField
              type="number"
              label="Sort order"
              value={sortOrder}
              onChange={(event) => setSortOrder(Number(event.target.value))}
              sx={{ width: 140 }}
            />

            <Stack direction="row" gap={1} alignItems="center">
              <Switch checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />
              <Typography variant="body2">Active</Typography>
            </Stack>
          </Stack>

          {category && category.subcategories.length > 0 && (
            <Box>
              <Typography variant="subtitle2" gutterBottom>
                Subcategories
              </Typography>
              <Stack direction="row" gap={0.5} flexWrap="wrap">
                {category.subcategories.map((sub) => (
                  <Chip
                    key={sub.id}
                    label={`${sub.name}${sub.recordCount > 0 ? ` · ${sub.recordCount}` : ''}`}
                    size="small"
                    variant={sub.isActive ? 'filled' : 'outlined'}
                  />
                ))}
              </Stack>
              <Typography variant="caption" color="text.secondary">
                Subcategories are managed through the API for now.
              </Typography>
            </Box>
          )}
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
