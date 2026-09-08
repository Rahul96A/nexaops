import { useCallback } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import {
  Box,
  Button,
  Card,
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
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import SearchIcon from '@mui/icons-material/Search';
import { requestsApi } from '@/api/requests';
import { PageHeader } from '@/components/PageHeader';
import { EmptyState } from '@/components/EmptyState';
import { ErrorState } from '@/components/ErrorState';
import { PriorityChip, UserChip } from '@/components/StatusChips';
import { RequestStatusChip } from './RequestChips';
import { formatCurrency, formatRelative } from '@/utils/format';

const scopes = [
  { value: '', label: 'All' },
  { value: 'my-work', label: 'My work' },
  { value: 'awaiting-approval', label: 'Awaiting approval' },
  { value: 'unassigned', label: 'Unassigned' },
  { value: 'raised-by-me', label: 'Raised by me' },
];

/**
 * The request queue.
 *
 * Filter state lives in the URL, so a view is shareable and the back button behaves, exactly as
 * on the incident queue.
 */
export function RequestListPage() {
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

      // Any filter change returns to the first page; staying on page 4 of a narrower result set
      // shows an empty table and reads as "no results".
      if (!('page' in changes)) {
        next.delete('page');
      }

      setParams(next, { replace: true });
    },
    [params, setParams],
  );

  const { data, isLoading, isFetching, error, refetch } = useQuery({
    queryKey: ['requests', scope, search, page, pageSize],
    queryFn: ({ signal }) =>
      requestsApi.search(
        { scope: scope || undefined, search: search || undefined, page, pageSize },
        signal,
      ),
    placeholderData: keepPreviousData,
  });

  return (
    <Box>
      <PageHeader
        title="Service requests"
        subtitle="Things people have asked for, and where each one has got to."
        actions={
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => navigate('/catalog')}>
            Order from catalogue
          </Button>
        }
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
          placeholder="Search by number, title or description"
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
          title="No requests here"
          description={
            scope || search
              ? 'Nothing matches these filters.'
              : 'Nothing has been requested yet. Order something from the catalogue to get started.'
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
                  <TableCell>Priority</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell>For</TableCell>
                  <TableCell>Assignee</TableCell>
                  <TableCell align="right">Cost</TableCell>
                  <TableCell>Raised</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {data.items.map((request) => (
                  <TableRow
                    key={request.id}
                    hover
                    sx={{ cursor: 'pointer' }}
                    onClick={() => navigate(`/requests/${request.id}`)}
                  >
                    <TableCell>
                      <Typography variant="body2" fontFamily="monospace">
                        {request.number}
                      </Typography>
                    </TableCell>
                    <TableCell>
                      <Typography variant="body2">{request.title}</Typography>
                      {request.itemCount > 1 && (
                        <Typography variant="caption" color="text.secondary">
                          {request.itemCount} items
                        </Typography>
                      )}
                    </TableCell>
                    <TableCell>
                      <PriorityChip priority={request.priority} />
                    </TableCell>
                    <TableCell>
                      <RequestStatusChip status={request.status} />
                    </TableCell>
                    <TableCell>
                      <Typography variant="body2">{request.requestedForName}</Typography>
                    </TableCell>
                    <TableCell>
                      <UserChip
                        name={request.assignedToName ?? null}
                        color={request.assignedToAvatarColor ?? undefined}
                      />
                    </TableCell>
                    <TableCell align="right">
                      <Typography variant="body2">
                        {request.totalCost != null ? formatCurrency(request.totalCost) : '—'}
                      </Typography>
                    </TableCell>
                    <TableCell>
                      <Typography variant="body2" color="text.secondary">
                        {formatRelative(request.createdAt)}
                      </Typography>
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
            onRowsPerPageChange={(event) =>
              update({ pageSize: event.target.value, page: null })
            }
          />
        </Card>
      )}
    </Box>
  );
}
