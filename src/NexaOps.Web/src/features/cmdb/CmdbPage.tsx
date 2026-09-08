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
import ReportProblemOutlinedIcon from '@mui/icons-material/ReportProblemOutlined';
import { cmdbApi } from '@/api/cmdb';
import { PageHeader } from '@/components/PageHeader';
import { EmptyState } from '@/components/EmptyState';
import { ErrorState } from '@/components/ErrorState';
import { StatCard } from '@/components/StatCard';
import { CiStatusChip, CriticalityChip } from './CmdbChips';
import { formatDate, humanise } from '@/utils/format';

const scopes = [
  { value: '', label: 'All' },
  { value: 'critical', label: 'Critical' },
  { value: 'impaired', label: 'Impaired' },
  { value: 'out-of-support', label: 'Out of support' },
  { value: 'unowned', label: 'Unowned' },
];

/**
 * The configuration item register.
 *
 * The counters lead with the two numbers a CMDB owner is judged on: how much of the live estate
 * has fallen out of support, and how much of it nobody is accountable for.
 */
export function CmdbPage() {
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

  const { data: summary } = useQuery({
    queryKey: ['cmdb-summary'],
    queryFn: ({ signal }) => cmdbApi.summary(signal),
  });

  const { data, isLoading, isFetching, error, refetch } = useQuery({
    queryKey: ['cmdb', scope, search, page, pageSize],
    queryFn: ({ signal }) =>
      cmdbApi.search(
        { scope: scope || undefined, search: search || undefined, page, pageSize },
        signal,
      ),
    placeholderData: keepPreviousData,
  });

  return (
    <Box>
      <PageHeader
        title="Configuration items"
        subtitle="What the estate is made of, and what depends on what."
      />

      {summary && (
        <Stack direction="row" gap={2} sx={{ mb: 3 }} flexWrap="wrap">
          <StatCard label="Items" value={summary.totalItems} />
          <StatCard label="Operational" value={summary.operational} />
          <StatCard label="Impaired" value={summary.impaired} tone="warning" />
          <StatCard label="Critical" value={summary.criticalItems} />
          <StatCard label="Out of support" value={summary.outOfSupport} tone="critical" />
          <StatCard label="Unowned" value={summary.unowned} tone="warning" />
        </Stack>
      )}

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
          placeholder="Search by name, number or serial number"
          defaultValue={search}
          onBlur={(event) => update({ search: event.target.value })}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              update({ search: (event.target as HTMLInputElement).value });
            }
          }}
          sx={{ minWidth: 340 }}
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
          title="Nothing here"
          description={
            scope || search
              ? 'Nothing matches these filters.'
              : 'The CMDB is empty. Items are added through the API.'
          }
        />
      )}

      {data && data.items.length > 0 && (
        <Card variant="outlined">
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Name</TableCell>
                  <TableCell>Type</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell>Criticality</TableCell>
                  <TableCell>Environment</TableCell>
                  <TableCell>Owner</TableCell>
                  <TableCell>Support until</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {data.items.map((item) => (
                  <TableRow
                    key={item.id}
                    hover
                    sx={{ cursor: 'pointer' }}
                    onClick={() => navigate(`/cmdb/${item.id}`)}
                  >
                    <TableCell>
                      <Typography variant="body2" fontWeight={600}>
                        {item.name}
                      </Typography>
                      <Typography variant="caption" color="text.secondary" fontFamily="monospace">
                        {item.number}
                      </Typography>
                    </TableCell>
                    <TableCell>
                      <Typography variant="body2">{humanise(item.type)}</Typography>
                    </TableCell>
                    <TableCell>
                      <CiStatusChip status={item.status} />
                    </TableCell>
                    <TableCell>
                      <CriticalityChip criticality={item.criticality} />
                    </TableCell>
                    <TableCell>
                      <Typography variant="body2" color="text.secondary">
                        {item.environment ?? '—'}
                      </Typography>
                    </TableCell>
                    <TableCell>
                      {item.ownerName ? (
                        <Typography variant="body2">{item.ownerName}</Typography>
                      ) : (
                        <Chip label="Unowned" size="small" color="warning" variant="outlined" />
                      )}
                    </TableCell>
                    <TableCell>
                      <Stack direction="row" gap={0.5} alignItems="center">
                        <Typography
                          variant="body2"
                          color={item.isOutOfSupport ? 'error' : 'text.secondary'}
                        >
                          {item.supportExpiresOn ? formatDate(item.supportExpiresOn) : '—'}
                        </Typography>
                        {item.isOutOfSupport && (
                          <Tooltip title="Support cover has lapsed">
                            <ReportProblemOutlinedIcon fontSize="small" color="error" />
                          </Tooltip>
                        )}
                      </Stack>
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
