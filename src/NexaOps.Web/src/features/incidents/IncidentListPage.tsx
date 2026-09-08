import { useCallback, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import {
  Box,
  Button,
  Card,
  Chip,
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
  TableSortLabel,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import SearchIcon from '@mui/icons-material/Search';
import ClearIcon from '@mui/icons-material/Clear';
import WhatshotOutlinedIcon from '@mui/icons-material/WhatshotOutlined';
import { incidentsApi } from '@/api/incidents';
import { referenceApi } from '@/api/reference';
import type {
  IncidentSearchParams,
  IncidentStatus,
  IncidentViewScope,
  Priority,
  SortDirection,
} from '@/api/types';
import { PageHeader } from '@/components/PageHeader';
import { ErrorState } from '@/components/ErrorState';
import { EmptyState } from '@/components/EmptyState';
import { PriorityChip, SlaBadge, StatusChip, UserChip } from '@/components/StatusChips';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { formatRelative, humanise } from '@/utils/format';
import { useDebounced } from '@/utils/useDebounced';

const SCOPES: { value: IncidentViewScope; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'AssignedToMe', label: 'Assigned to me' },
  { value: 'MyTeam', label: 'My team' },
  { value: 'Unassigned', label: 'Unassigned' },
  { value: 'Breached', label: 'Breached' },
  { value: 'DueSoon', label: 'Due soon' },
  { value: 'RaisedByMe', label: 'Raised by me' },
];

const STATUSES: IncidentStatus[] = [
  'New',
  'Assigned',
  'InProgress',
  'Pending',
  'Resolved',
  'Closed',
  'Cancelled',
];

const PRIORITIES: Priority[] = ['P1Critical', 'P2High', 'P3Moderate', 'P4Low', 'P5Planning'];

/** Columns the server will sort by. Anything else is rejected by the API rather than guessed at. */
const SORTABLE = {
  number: 'Number',
  title: 'Summary',
  priority: 'Priority',
  status: 'Status',
  createdAt: 'Raised',
  updatedAt: 'Updated',
} as const;

/**
 * The incident queue.
 *
 * Filter state lives in the URL rather than in component state, so a queue an agent has tuned
 * can be bookmarked, shared with a colleague, and survives a page reload - which is what people
 * mean by a "saved view" most of the time.
 */
export function IncidentListPage() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [searchText, setSearchText] = useState(() => searchParams.get('search') ?? '');

  // Typing should not fire a query per keystroke.
  const debouncedSearch = useDebounced(searchText, 350);

  const scope = (searchParams.get('scope') as IncidentViewScope | null) ?? 'All';
  const statuses = searchParams.getAll('status') as IncidentStatus[];
  const priorities = searchParams.getAll('priority') as Priority[];
  const groupId = searchParams.get('assignmentGroupId') ?? '';
  const categoryId = searchParams.get('categoryId') ?? '';
  const openOnly = searchParams.get('openOnly') === 'true';
  const page = Number(searchParams.get('page') ?? '1');
  const pageSize = Number(searchParams.get('pageSize') ?? '25');
  const sortBy = searchParams.get('sortBy') ?? 'createdAt';
  const sortDirection = (searchParams.get('sortDirection') as SortDirection | null) ?? 'Descending';

  const updateParams = useCallback(
    (mutate: (params: URLSearchParams) => void, resetPage = true) => {
      const next = new URLSearchParams(searchParams);
      mutate(next);

      if (resetPage) {
        next.delete('page');
      }

      setSearchParams(next, { replace: true });
    },
    [searchParams, setSearchParams],
  );

  const query = useMemo<IncidentSearchParams>(
    () => ({
      search: debouncedSearch || undefined,
      scope,
      status: statuses.length > 0 ? statuses : undefined,
      priority: priorities.length > 0 ? priorities : undefined,
      assignmentGroupId: groupId || undefined,
      categoryId: categoryId || undefined,
      openOnly: openOnly || undefined,
      page,
      pageSize,
      sortBy,
      sortDirection,
    }),
    [
      debouncedSearch,
      scope,
      statuses,
      priorities,
      groupId,
      categoryId,
      openOnly,
      page,
      pageSize,
      sortBy,
      sortDirection,
    ],
  );

  const incidents = useQuery({
    queryKey: ['incidents', 'list', query],
    queryFn: ({ signal }) => incidentsApi.search(query, signal),
    // Keeps the previous page on screen while the next one loads, so the table does not
    // collapse to a spinner every time a filter changes.
    placeholderData: keepPreviousData,
  });

  const groups = useQuery({
    queryKey: ['reference', 'groups'],
    queryFn: ({ signal }) => referenceApi.groups('Assignment', signal),
    staleTime: 5 * 60_000,
  });

  const categories = useQuery({
    queryKey: ['reference', 'categories'],
    queryFn: ({ signal }) => referenceApi.categories('Incident', signal),
    staleTime: 5 * 60_000,
  });

  function toggleSort(field: string) {
    updateParams((params) => {
      const isSame = params.get('sortBy') === field;
      const currentDirection = params.get('sortDirection') ?? 'Descending';

      params.set('sortBy', field);
      params.set(
        'sortDirection',
        isSame && currentDirection === 'Descending' ? 'Ascending' : 'Descending',
      );
    });
  }

  function clearFilters() {
    setSearchText('');
    setSearchParams(new URLSearchParams(), { replace: true });
  }

  const activeFilterCount =
    statuses.length +
    priorities.length +
    (groupId ? 1 : 0) +
    (categoryId ? 1 : 0) +
    (openOnly ? 1 : 0) +
    (scope !== 'All' ? 1 : 0);

  return (
    <>
      <PageHeader
        title="Incidents"
        subtitle={
          incidents.data
            ? `${incidents.data.totalCount.toLocaleString('en-IN')} matching ${
                incidents.data.totalCount === 1 ? 'incident' : 'incidents'
              }`
            : 'Loading…'
        }
        actions={
          <Can permission={Permissions.incidentCreate}>
            <Button variant="contained" startIcon={<AddIcon />} onClick={() => navigate('/incidents/new')}>
              Raise incident
            </Button>
          </Can>
        }
      />

      <Card sx={{ mb: 2 }}>
        <Box sx={{ p: 2 }}>
          <Stack spacing={2}>
            <ToggleButtonGroup
              value={scope}
              exclusive
              size="small"
              onChange={(_, value: IncidentViewScope | null) => {
                if (value) {
                  updateParams((params) => {
                    if (value === 'All') {
                      params.delete('scope');
                    } else {
                      params.set('scope', value);
                    }
                  });
                }
              }}
              sx={{ flexWrap: 'wrap' }}
              aria-label="Queue view"
            >
              {SCOPES.map((option) => (
                <ToggleButton key={option.value} value={option.value} sx={{ px: 2 }}>
                  {option.label}
                </ToggleButton>
              ))}
            </ToggleButtonGroup>

            <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} alignItems={{ md: 'center' }}>
              <TextField
                value={searchText}
                onChange={(event) => {
                  setSearchText(event.target.value);
                  updateParams((params) => {
                    if (event.target.value) {
                      params.set('search', event.target.value);
                    } else {
                      params.delete('search');
                    }
                  });
                }}
                placeholder="Search number, summary or description"
                sx={{ flex: 1, minWidth: 260 }}
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

              <TextField
                select
                label="Status"
                value={statuses}
                slotProps={{ select: { multiple: true, renderValue: renderStatuses } }}
                onChange={(event) => {
                  const value = event.target.value as unknown as IncidentStatus[];
                  updateParams((params) => {
                    params.delete('status');
                    value.forEach((status) => params.append('status', status));
                  });
                }}
                sx={{ minWidth: 170 }}
              >
                {STATUSES.map((status) => (
                  <MenuItem key={status} value={status}>
                    {humanise(status)}
                  </MenuItem>
                ))}
              </TextField>

              <TextField
                select
                label="Priority"
                value={priorities}
                slotProps={{ select: { multiple: true, renderValue: renderPriorities } }}
                onChange={(event) => {
                  const value = event.target.value as unknown as Priority[];
                  updateParams((params) => {
                    params.delete('priority');
                    value.forEach((priority) => params.append('priority', priority));
                  });
                }}
                sx={{ minWidth: 170 }}
              >
                {PRIORITIES.map((priority) => (
                  <MenuItem key={priority} value={priority}>
                    {priority}
                  </MenuItem>
                ))}
              </TextField>

              <TextField
                select
                label="Group"
                value={groupId}
                onChange={(event) =>
                  updateParams((params) => {
                    if (event.target.value) {
                      params.set('assignmentGroupId', event.target.value);
                    } else {
                      params.delete('assignmentGroupId');
                    }
                  })
                }
                sx={{ minWidth: 180 }}
              >
                <MenuItem value="">Any group</MenuItem>
                {(groups.data ?? []).map((group) => (
                  <MenuItem key={group.id} value={group.id}>
                    {group.name}
                  </MenuItem>
                ))}
              </TextField>

              <TextField
                select
                label="Category"
                value={categoryId}
                onChange={(event) =>
                  updateParams((params) => {
                    if (event.target.value) {
                      params.set('categoryId', event.target.value);
                    } else {
                      params.delete('categoryId');
                    }
                  })
                }
                sx={{ minWidth: 200 }}
              >
                <MenuItem value="">Any category</MenuItem>
                {(categories.data ?? []).map((category) => (
                  <MenuItem key={category.id} value={category.id}>
                    {category.name}
                  </MenuItem>
                ))}
              </TextField>

              {activeFilterCount > 0 && (
                <Button size="small" startIcon={<ClearIcon />} onClick={clearFilters}>
                  Clear ({activeFilterCount})
                </Button>
              )}
            </Stack>
          </Stack>
        </Box>

        {incidents.isFetching && <LinearProgress />}

        {incidents.isError ? (
          <ErrorState error={incidents.error} onRetry={() => incidents.refetch()} />
        ) : incidents.data && incidents.data.items.length === 0 ? (
          <EmptyState
            title="No incidents match these filters"
            description="Try widening the search, or clear the filters to see the whole queue."
            action={
              activeFilterCount > 0 ? (
                <Button variant="outlined" onClick={clearFilters}>
                  Clear filters
                </Button>
              ) : undefined
            }
          />
        ) : (
          <>
            <TableContainer>
              <Table size="small" stickyHeader>
                <TableHead>
                  <TableRow>
                    {Object.entries(SORTABLE).map(([field, label]) => (
                      <TableCell key={field} sortDirection={sortBy === field ? sortLabel(sortDirection) : false}>
                        <TableSortLabel
                          active={sortBy === field}
                          direction={sortBy === field ? sortLabel(sortDirection) : 'desc'}
                          onClick={() => toggleSort(field)}
                        >
                          {label}
                        </TableSortLabel>
                      </TableCell>
                    ))}
                    <TableCell>Requester</TableCell>
                    <TableCell>Assignee</TableCell>
                    <TableCell>Group</TableCell>
                    <TableCell>SLA</TableCell>
                  </TableRow>
                </TableHead>

                <TableBody>
                  {(incidents.data?.items ?? []).map((incident) => (
                    <TableRow
                      key={incident.id}
                      hover
                      onClick={() => navigate(`/incidents/${incident.id}`)}
                      sx={{ cursor: 'pointer' }}
                    >
                      <TableCell sx={{ whiteSpace: 'nowrap' }}>
                        <Stack direction="row" spacing={0.75} alignItems="center">
                          {incident.isMajorIncident && (
                            <Tooltip title="Major incident">
                              <WhatshotOutlinedIcon color="error" sx={{ fontSize: 16 }} />
                            </Tooltip>
                          )}
                          <Typography
                            variant="body2"
                            sx={{ fontFamily: 'monospace', fontSize: '0.8125rem', fontWeight: 600 }}
                          >
                            {incident.number}
                          </Typography>
                        </Stack>
                      </TableCell>

                      <TableCell sx={{ maxWidth: 380 }}>
                        <Typography variant="body2" noWrap title={incident.title}>
                          {incident.title}
                        </Typography>
                        {incident.categoryName && (
                          <Typography variant="caption" color="text.secondary" noWrap>
                            {incident.categoryName}
                            {incident.subcategoryName ? ` · ${incident.subcategoryName}` : ''}
                          </Typography>
                        )}
                      </TableCell>

                      <TableCell>
                        <PriorityChip priority={incident.priority} />
                      </TableCell>

                      <TableCell>
                        <StatusChip status={incident.status} />
                      </TableCell>

                      <TableCell sx={{ whiteSpace: 'nowrap' }}>
                        <Tooltip title={new Date(incident.createdAt).toString()}>
                          <Typography variant="caption" color="text.secondary">
                            {formatRelative(incident.createdAt)}
                          </Typography>
                        </Tooltip>
                      </TableCell>

                      <TableCell sx={{ whiteSpace: 'nowrap' }}>
                        <Typography variant="caption" color="text.secondary">
                          {formatRelative(incident.updatedAt ?? incident.createdAt)}
                        </Typography>
                      </TableCell>

                      <TableCell>
                        <UserChip name={incident.requesterName} size={22} />
                      </TableCell>

                      <TableCell>
                        <UserChip name={incident.assignedToName} size={22} />
                      </TableCell>

                      <TableCell>
                        <Typography variant="caption" color="text.secondary" noWrap>
                          {incident.assignmentGroupName ?? '—'}
                        </Typography>
                      </TableCell>

                      <TableCell>
                        <SlaBadge breached={incident.hasBreachedSla} dueAt={incident.nextSlaDueAt} />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>

            <TablePagination
              component="div"
              count={incidents.data?.totalCount ?? 0}
              page={Math.max(0, page - 1)}
              rowsPerPage={pageSize}
              rowsPerPageOptions={[10, 25, 50, 100]}
              onPageChange={(_, nextPage) =>
                updateParams((params) => params.set('page', String(nextPage + 1)), false)
              }
              onRowsPerPageChange={(event) =>
                updateParams((params) => params.set('pageSize', event.target.value))
              }
            />
          </>
        )}
      </Card>
    </>
  );
}

function sortLabel(direction: SortDirection): 'asc' | 'desc' {
  return direction === 'Ascending' ? 'asc' : 'desc';
}

function renderStatuses(selected: unknown): React.ReactNode {
  const values = selected as IncidentStatus[];

  if (values.length === 0) {
    return <Typography variant="body2" color="text.disabled">Any status</Typography>;
  }

  return (
    <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap' }}>
      {values.map((status) => (
        <Chip key={status} label={humanise(status)} size="small" />
      ))}
    </Stack>
  );
}

function renderPriorities(selected: unknown): React.ReactNode {
  const values = selected as Priority[];

  if (values.length === 0) {
    return <Typography variant="body2" color="text.disabled">Any priority</Typography>;
  }

  return (
    <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap' }}>
      {values.map((priority) => (
        <PriorityChip key={priority} priority={priority} />
      ))}
    </Stack>
  );
}
