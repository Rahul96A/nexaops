import { useCallback } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import {
  Box,
  Card,
  Chip,
  InputAdornment,
  LinearProgress,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TablePagination,
  TableRow,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import PowerSettingsNewOutlinedIcon from '@mui/icons-material/PowerSettingsNewOutlined';
import { changesApi } from '@/api/changes';
import { PageHeader } from '@/components/PageHeader';
import { EmptyState } from '@/components/EmptyState';
import { ErrorState } from '@/components/ErrorState';
import { UserChip } from '@/components/StatusChips';
import { ChangeRiskChip, ChangeStatusChip, ChangeTypeChip } from './ChangeChips';
import { formatDateTime } from '@/utils/format';

const scopes = [
  { value: '', label: 'All' },
  { value: 'this-week', label: 'This week' },
  { value: 'awaiting-approval', label: 'Awaiting approval' },
  { value: 'awaiting-review', label: 'Awaiting review' },
  { value: 'my-work', label: 'My work' },
  { value: 'emergency', label: 'Emergency' },
];

/**
 * The change queue, which doubles as the change calendar.
 *
 * Sorted by window rather than by creation date, because "what is happening and when" is the
 * question this list exists to answer. Unscheduled changes sort last: a null window is not the
 * earliest window.
 */
export function ChangeListPage() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();

  const scope = params.get('scope') ?? '';
  const search = params.get('search') ?? '';
  const page = Number(params.get('page') ?? '1');
  const pageSize = Number(params.get('pageSize') ?? '25');

  const update = useCallback(
    (changes: Record<string, string | null>) => {
      const next = new URLSearchParams(params);

      for (const [key, value] of Object.entries(changes)) {
        if (value === null || value === '') {
          next.delete(key);
        } else {
          next.set(key, value);
        }
      }

      if (!('page' in changes)) {
        next.delete('page');
      }

      setParams(next, { replace: true });
    },
    [params, setParams],
  );

  const { data, isLoading, isFetching, error, refetch } = useQuery({
    queryKey: ['changes', scope, search, page, pageSize],
    queryFn: ({ signal }) =>
      changesApi.search(
        { scope: scope || undefined, search: search || undefined, page, pageSize },
        signal,
      ),
    placeholderData: keepPreviousData,
  });

  return (
    <Box>
      <PageHeader
        title="Changes"
        subtitle="What is being changed, when, and how much risk it carries."
      />

      <Stack direction={{ xs: 'column', md: 'row' }} gap={2} sx={{ mb: 2 }} alignItems="center">
        <ToggleButtonGroup
          exclusive
          size="small"
          value={scope}
          onChange={(_, value: string | null) => update({ scope: value ?? '' })}
        >
          {scopes.map((option) => (
            <ToggleButton key={option.value || 'all'} value={option.value}>
              {option.label}
            </ToggleButton>
          ))}
        </ToggleButtonGroup>

        <TextField
          size="small"
          placeholder="Search by number or title"
          defaultValue={search}
          onBlur={(event) => update({ search: event.target.value })}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              update({ search: (event.target as HTMLInputElement).value });
            }
          }}
          sx={{ minWidth: 320 }}
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
      </Stack>

      {(isLoading || isFetching) && <LinearProgress />}

      {error && <ErrorState error={error} onRetry={() => void refetch()} />}

      {data && data.items.length === 0 && (
        <EmptyState
          title="No changes here"
          description={
            scope || search
              ? 'Nothing matches these filters.'
              : 'Nothing is scheduled. Raise a change to plan work on a service.'
          }
        />
      )}

      {data && data.items.length > 0 && (
        <Card variant="outlined">
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Number</TableCell>
                  <TableCell>Summary</TableCell>
                  <TableCell>Type</TableCell>
                  <TableCell>Risk</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell>Assignee</TableCell>
                  <TableCell>Window</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {data.items.map((change) => (
                  <TableRow
                    key={change.id}
                    hover
                    sx={{ cursor: 'pointer' }}
                    onClick={() => navigate(`/changes/${change.id}`)}
                  >
                    <TableCell>
                      <Typography variant="body2" fontFamily="monospace">
                        {change.number}
                      </Typography>
                    </TableCell>
                    <TableCell>
                      <Stack direction="row" gap={1} alignItems="center">
                        <Typography variant="body2">{change.title}</Typography>

                        {change.requiresDowntime && (
                          <Tooltip title="This change needs a service outage">
                            <PowerSettingsNewOutlinedIcon fontSize="small" color="warning" />
                          </Tooltip>
                        )}
                      </Stack>
                    </TableCell>
                    <TableCell>
                      <ChangeTypeChip type={change.type} />
                    </TableCell>
                    <TableCell>
                      <ChangeRiskChip risk={change.risk} />
                    </TableCell>
                    <TableCell>
                      <ChangeStatusChip status={change.status} />
                    </TableCell>
                    <TableCell>
                      <UserChip
                        name={change.assignedToName ?? null}
                        color={change.assignedToAvatarColor ?? undefined}
                      />
                    </TableCell>
                    <TableCell>
                      {change.plannedStartAt ? (
                        <Typography variant="body2" color="text.secondary">
                          {formatDateTime(change.plannedStartAt)}
                        </Typography>
                      ) : (
                        <Chip label="Not scheduled" size="small" variant="outlined" />
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>

          <TablePagination
            component="div"
            count={data.totalCount}
            page={Math.max(0, page - 1)}
            rowsPerPage={pageSize}
            rowsPerPageOptions={[10, 25, 50, 100]}
            onPageChange={(_, next) => update({ page: String(next + 1) })}
            onRowsPerPageChange={(event) => update({ pageSize: event.target.value, page: null })}
          />
        </Card>
      )}
    </Box>
  );
}
