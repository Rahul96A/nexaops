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
import LightbulbOutlinedIcon from '@mui/icons-material/LightbulbOutlined';
import { problemsApi } from '@/api/problems';
import { PageHeader } from '@/components/PageHeader';
import { EmptyState } from '@/components/EmptyState';
import { ErrorState } from '@/components/ErrorState';
import { PriorityChip, UserChip } from '@/components/StatusChips';
import { ProblemStatusChip } from './ProblemChips';
import { formatRelative } from '@/utils/format';

const scopes = [
  { value: '', label: 'All' },
  { value: 'known-errors', label: 'Known errors' },
  { value: 'my-work', label: 'My work' },
  { value: 'owned-by-me', label: 'I own' },
  { value: 'unassigned', label: 'Unassigned' },
];

/**
 * The problem queue.
 *
 * Search covers root cause and workaround text as well as the title, because the question an
 * agent is actually asking mid-call is "has anyone seen this before".
 */
export function ProblemListPage() {
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
    queryKey: ['problems', scope, search, page, pageSize],
    queryFn: ({ signal }) =>
      problemsApi.search(
        { scope: scope || undefined, search: search || undefined, page, pageSize },
        signal,
      ),
    placeholderData: keepPreviousData,
  });

  return (
    <Box>
      <PageHeader
        title="Problems"
        subtitle="Underlying causes, published workarounds, and the permanent fixes that remove them."
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
          placeholder="Search titles, root causes and workarounds"
          defaultValue={search}
          onBlur={(event) => update({ search: event.target.value })}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              update({ search: (event.target as HTMLInputElement).value });
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
      </Stack>

      {(isLoading || isFetching) && <LinearProgress />}

      {error && <ErrorState error={error} onRetry={() => void refetch()} />}

      {data && data.items.length === 0 && (
        <EmptyState
          title="No problems here"
          description={
            scope || search
              ? 'Nothing matches these filters.'
              : 'Raise a problem from a recurring incident to start investigating its cause.'
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
                  <TableCell>Owner</TableCell>
                  <TableCell>Assignee</TableCell>
                  <TableCell align="right">Incidents</TableCell>
                  <TableCell>Raised</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {data.items.map((problem) => (
                  <TableRow
                    key={problem.id}
                    hover
                    sx={{ cursor: 'pointer' }}
                    onClick={() => navigate(`/problems/${problem.id}`)}
                  >
                    <TableCell>
                      <Typography variant="body2" fontFamily="monospace">
                        {problem.number}
                      </Typography>
                    </TableCell>
                    <TableCell>
                      <Stack direction="row" gap={1} alignItems="center">
                        <Typography variant="body2">{problem.title}</Typography>

                        {problem.hasWorkaround && (
                          <Tooltip title="A workaround is published for this problem">
                            <LightbulbOutlinedIcon fontSize="small" color="success" />
                          </Tooltip>
                        )}

                        {problem.isMajorProblem && (
                          <Chip label="Major" size="small" color="error" variant="outlined" />
                        )}
                      </Stack>
                    </TableCell>
                    <TableCell>
                      <PriorityChip priority={problem.priority} />
                    </TableCell>
                    <TableCell>
                      <ProblemStatusChip status={problem.status} />
                    </TableCell>
                    <TableCell>
                      <Typography variant="body2">{problem.ownerName ?? '—'}</Typography>
                    </TableCell>
                    <TableCell>
                      <UserChip
                        name={problem.assignedToName ?? null}
                        color={problem.assignedToAvatarColor ?? undefined}
                      />
                    </TableCell>
                    <TableCell align="right">
                      <Typography variant="body2">{problem.linkedIncidentCount}</Typography>
                    </TableCell>
                    <TableCell>
                      <Typography variant="body2" color="text.secondary">
                        {formatRelative(problem.createdAt)}
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
            onRowsPerPageChange={(event) => update({ pageSize: event.target.value, page: null })}
          />
        </Card>
      )}
    </Box>
  );
}
