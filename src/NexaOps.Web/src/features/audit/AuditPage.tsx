import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import {
  Alert,
  Box,
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
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import { auditApi } from '@/api/notifications';
import { PageHeader } from '@/components/PageHeader';
import { ErrorState } from '@/components/ErrorState';
import { EmptyState } from '@/components/EmptyState';
import { formatLongDateTime, humanise } from '@/utils/format';
import { useDebounced } from '@/utils/useDebounced';

const ACTIONS = [
  'Create',
  'Update',
  'Archive',
  'Assign',
  'StatusChange',
  'Comment',
  'Login',
  'LoginFailed',
  'Logout',
  'AccessDenied',
  'SecurityEvent',
  'AiToolExecution',
  'AiAgentAction',
  'Export',
];

const SOURCES = ['Api', 'Ai', 'Workflow', 'System', 'Integration', 'Import'];
const OUTCOMES = ['Success', 'Failure', 'Denied'];

/**
 * The tenant audit trail.
 *
 * Reading this requires the audit permission, which no role holds implicitly. There is no
 * create, edit or delete action on this screen because none exists in the API: the trail is
 * append-only by construction.
 */
export function AuditPage() {
  const [search, setSearch] = useState('');
  const [action, setAction] = useState('');
  const [source, setSource] = useState('');
  const [outcome, setOutcome] = useState('');
  const [entityType, setEntityType] = useState('');
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(25);

  const debouncedSearch = useDebounced(search, 350);

  const events = useQuery({
    queryKey: ['audit', 'search', { debouncedSearch, action, source, outcome, entityType, page, pageSize }],
    queryFn: ({ signal }) =>
      auditApi.search(
        {
          search: debouncedSearch || undefined,
          action: action || undefined,
          source: source || undefined,
          outcome: outcome || undefined,
          entityType: entityType || undefined,
          page: page + 1,
          pageSize,
        },
        signal,
      ),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader
        title="Audit trail"
        subtitle="Every create, change, sign-in, permission denial and AI action, recorded and never editable."
      />

      <Alert severity="info" sx={{ mb: 2 }}>
        Audit records are append-only. NexaOps exposes no path to modify or delete them, and
        reading them requires a permission that is granted separately from any other.
      </Alert>

      <Card>
        <Box sx={{ p: 2 }}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5}>
            <TextField
              value={search}
              onChange={(event) => {
                setSearch(event.target.value);
                setPage(0);
              }}
              placeholder="Search record, actor or message"
              sx={{ flex: 1, minWidth: 240 }}
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
              label="Action"
              value={action}
              onChange={(event) => {
                setAction(event.target.value);
                setPage(0);
              }}
              sx={{ minWidth: 170 }}
            >
              <MenuItem value="">Any action</MenuItem>
              {ACTIONS.map((option) => (
                <MenuItem key={option} value={option}>
                  {humanise(option)}
                </MenuItem>
              ))}
            </TextField>

            <TextField
              select
              label="Record type"
              value={entityType}
              onChange={(event) => {
                setEntityType(event.target.value);
                setPage(0);
              }}
              sx={{ minWidth: 160 }}
            >
              <MenuItem value="">Any type</MenuItem>
              {['Incident', 'IncidentComment', 'User', 'Role', 'Group', 'AiTool', 'HttpRequest'].map(
                (option) => (
                  <MenuItem key={option} value={option}>
                    {humanise(option)}
                  </MenuItem>
                ),
              )}
            </TextField>

            <TextField
              select
              label="Source"
              value={source}
              onChange={(event) => {
                setSource(event.target.value);
                setPage(0);
              }}
              sx={{ minWidth: 140 }}
            >
              <MenuItem value="">Any source</MenuItem>
              {SOURCES.map((option) => (
                <MenuItem key={option} value={option}>
                  {option}
                </MenuItem>
              ))}
            </TextField>

            <TextField
              select
              label="Outcome"
              value={outcome}
              onChange={(event) => {
                setOutcome(event.target.value);
                setPage(0);
              }}
              sx={{ minWidth: 140 }}
            >
              <MenuItem value="">Any outcome</MenuItem>
              {OUTCOMES.map((option) => (
                <MenuItem key={option} value={option}>
                  {option}
                </MenuItem>
              ))}
            </TextField>
          </Stack>
        </Box>

        {events.isFetching && <LinearProgress />}

        {events.isError ? (
          <ErrorState error={events.error} onRetry={() => events.refetch()} />
        ) : events.data && events.data.items.length === 0 ? (
          <EmptyState title="No audit events match these filters" />
        ) : (
          <>
            <TableContainer>
              <Table size="small" stickyHeader>
                <TableHead>
                  <TableRow>
                    <TableCell>When</TableCell>
                    <TableCell>Actor</TableCell>
                    <TableCell>Action</TableCell>
                    <TableCell>Record</TableCell>
                    <TableCell>Changed</TableCell>
                    <TableCell>Source</TableCell>
                    <TableCell>Outcome</TableCell>
                    <TableCell>IP</TableCell>
                  </TableRow>
                </TableHead>

                <TableBody>
                  {(events.data?.items ?? []).map((event) => (
                    <TableRow key={event.id} hover>
                      <TableCell sx={{ whiteSpace: 'nowrap' }}>
                        <Typography variant="caption">
                          {formatLongDateTime(event.occurredAt)}
                        </Typography>
                      </TableCell>

                      <TableCell>
                        <Typography variant="body2" noWrap>
                          {event.actorDisplayName ?? 'System'}
                        </Typography>
                      </TableCell>

                      <TableCell>
                        <Chip label={humanise(event.action)} size="small" variant="outlined" />
                      </TableCell>

                      <TableCell sx={{ maxWidth: 200 }}>
                        <Typography variant="body2" noWrap>
                          {event.entityLabel ?? event.entityType}
                        </Typography>
                        <Typography variant="caption" color="text.secondary">
                          {event.entityType}
                        </Typography>
                      </TableCell>

                      <TableCell sx={{ maxWidth: 260 }}>
                        <Tooltip
                          title={
                            event.beforeJson || event.afterJson
                              ? `Before: ${event.beforeJson ?? 'n/a'} | After: ${event.afterJson ?? 'n/a'}`
                              : (event.message ?? '')
                          }
                        >
                          <Typography variant="caption" noWrap display="block">
                            {event.changedFields
                              ? event.changedFields
                                  .split(',')
                                  .map((field) => humanise(field.trim()))
                                  .join(', ')
                              : (event.message ?? '—')}
                          </Typography>
                        </Tooltip>
                      </TableCell>

                      <TableCell>
                        <Typography variant="caption" color="text.secondary">
                          {event.source}
                        </Typography>
                      </TableCell>

                      <TableCell>
                        <Chip
                          label={event.outcome}
                          size="small"
                          color={
                            event.outcome === 'Success'
                              ? 'success'
                              : event.outcome === 'Denied'
                                ? 'warning'
                                : 'error'
                          }
                          variant="outlined"
                        />
                      </TableCell>

                      <TableCell>
                        <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>
                          {event.ipAddress ?? '—'}
                        </Typography>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>

            <TablePagination
              component="div"
              count={events.data?.totalCount ?? 0}
              page={page}
              rowsPerPage={pageSize}
              rowsPerPageOptions={[25, 50, 100]}
              onPageChange={(_, nextPage) => setPage(nextPage)}
              onRowsPerPageChange={(event) => {
                setPageSize(Number(event.target.value));
                setPage(0);
              }}
            />
          </>
        )}
      </Card>
    </>
  );
}
