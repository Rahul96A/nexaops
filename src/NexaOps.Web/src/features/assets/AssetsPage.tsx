import { useCallback } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Card,
  CardContent,
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
  Tab,
  Tabs,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import AutorenewOutlinedIcon from '@mui/icons-material/AutorenewOutlined';
import { assetsApi } from '@/api/assets';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { PageHeader } from '@/components/PageHeader';
import { EmptyState } from '@/components/EmptyState';
import { ErrorState } from '@/components/ErrorState';
import { StatCard } from '@/components/StatCard';
import { AssetStatusChip, ComplianceChip } from '@/features/cmdb/CmdbChips';
import { formatCurrency, formatDate, humanise } from '@/utils/format';

const scopes = [
  { value: '', label: 'All' },
  { value: 'assigned-to-me', label: 'Issued to me' },
  { value: 'in-stock', label: 'In stock' },
  { value: 'in-repair', label: 'In repair' },
  { value: 'due-refresh', label: 'Due refresh' },
  { value: 'out-of-warranty', label: 'Out of warranty' },
];

/**
 * The asset register, with licences on a second tab.
 *
 * Licences share the page rather than getting their own navigation entry because the question
 * they answer — what do we own and are we covered for it — is the same question.
 */
export function AssetsPage() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();

  const tab = params.get('tab') === 'licences' ? 'licences' : 'assets';
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
    queryKey: ['asset-summary'],
    queryFn: ({ signal }) => assetsApi.summary(signal),
  });

  const { data, isLoading, isFetching, error, refetch } = useQuery({
    queryKey: ['assets', scope, search, page, pageSize],
    queryFn: ({ signal }) =>
      assetsApi.search(
        { scope: scope || undefined, search: search || undefined, page, pageSize },
        signal,
      ),
    placeholderData: keepPreviousData,
    enabled: tab === 'assets',
  });

  const { data: licences, isLoading: licencesLoading } = useQuery({
    queryKey: ['licences'],
    queryFn: ({ signal }) => assetsApi.licences(signal),
    enabled: tab === 'licences',
  });

  return (
    <Box>
      <PageHeader
        title="Assets"
        subtitle="What the organisation owns, who holds it, and whether the licences cover it."
      />

      {summary && (
        <Stack direction="row" gap={2} sx={{ mb: 3 }} flexWrap="wrap">
          <StatCard label="Assets" value={summary.totalAssets} />
          <StatCard label="Issued" value={summary.assigned} />
          <StatCard label="In stock" value={summary.inStock} />
          <StatCard label="In repair" value={summary.inRepair} tone="warning" />
          <StatCard label="Due refresh" value={summary.dueForRefresh} tone="warning" />
          <StatCard label="Out of warranty" value={summary.outOfWarranty} tone="critical" />
        </Stack>
      )}

      <Tabs
        value={tab}
        onChange={(_, value: string) => update({ tab: value === 'assets' ? null : value })}
        sx={{ mb: 2 }}
      >
        <Tab label="Register" value="assets" />
        <Can permission={Permissions.licenceRead}>
          <Tab label="Licences" value="licences" />
        </Can>
      </Tabs>

      {tab === 'assets' && (
        <>
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
              placeholder="Search by tag, name or serial number"
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
                  : 'The register is empty. Assets are added through the API.'
              }
            />
          )}

          {data && data.items.length > 0 && (
            <Card variant="outlined">
              <TableContainer>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Tag</TableCell>
                      <TableCell>Name</TableCell>
                      <TableCell>Kind</TableCell>
                      <TableCell>Status</TableCell>
                      <TableCell>Held by</TableCell>
                      <TableCell>Warranty</TableCell>
                      <TableCell align="right">Cost</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {data.items.map((asset) => (
                      <TableRow
                        key={asset.id}
                        hover
                        sx={{ cursor: 'pointer' }}
                        onClick={() => navigate(`/assets/${asset.id}`)}
                      >
                        <TableCell>
                          <Typography variant="body2" fontFamily="monospace">
                            {asset.assetTag}
                          </Typography>
                        </TableCell>
                        <TableCell>
                          <Stack direction="row" gap={1} alignItems="center">
                            <Typography variant="body2">{asset.name}</Typography>
                            {asset.isDueForRefresh && (
                              <Tooltip title="Past its expected working life">
                                <AutorenewOutlinedIcon fontSize="small" color="warning" />
                              </Tooltip>
                            )}
                          </Stack>
                        </TableCell>
                        <TableCell>
                          <Typography variant="body2">{humanise(asset.kind)}</Typography>
                        </TableCell>
                        <TableCell>
                          <AssetStatusChip status={asset.status} />
                        </TableCell>
                        <TableCell>
                          <Typography variant="body2">{asset.assignedToName ?? '—'}</Typography>
                        </TableCell>
                        <TableCell>
                          <Typography
                            variant="body2"
                            color={asset.isOutOfWarranty ? 'error' : 'text.secondary'}
                          >
                            {asset.warrantyExpiresOn ? formatDate(asset.warrantyExpiresOn) : '—'}
                          </Typography>
                        </TableCell>
                        <TableCell align="right">
                          <Typography variant="body2">
                            {asset.purchaseCost != null ? formatCurrency(asset.purchaseCost) : '—'}
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
        </>
      )}

      {tab === 'licences' && (
        <>
          {licencesLoading && <LinearProgress />}

          {summary && summary.overDeployedLicences > 0 && (
            <Alert severity="error" sx={{ mb: 2 }}>
              <strong>
                {summary.overDeployedLicences} licence
                {summary.overDeployedLicences === 1 ? '' : 's'} over-deployed by{' '}
                {summary.overDeployedSeats} seat{summary.overDeployedSeats === 1 ? '' : 's'}.
              </strong>{' '}
              {summary.exposureCost != null && (
                <>
                  Indicative exposure {formatCurrency(summary.exposureCost)} — a rough figure, not
                  a bill. Real remediation is negotiated.
                </>
              )}
            </Alert>
          )}

          {summary && summary.expiredLicences > 0 && (
            <Alert severity="error" sx={{ mb: 2 }}>
              <strong>
                {summary.expiredLicences} agreement
                {summary.expiredLicences === 1 ? ' has' : 's have'} lapsed.
              </strong>{' '}
              Every deployment against a lapsed agreement is unlicensed, whatever the seat count
              says.
            </Alert>
          )}

          {licences && licences.length === 0 && (
            <EmptyState
              title="No licences recorded"
              description="Licence agreements are recorded through the API."
            />
          )}

          <Stack gap={1.5}>
            {(licences ?? []).map((licence) => (
              <Card key={licence.id} variant="outlined">
                <CardContent>
                  <Stack
                    direction={{ xs: 'column', sm: 'row' }}
                    justifyContent="space-between"
                    alignItems={{ xs: 'flex-start', sm: 'center' }}
                    gap={1.5}
                  >
                    <Box>
                      <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap">
                        <Typography variant="subtitle2">{licence.productName}</Typography>
                        <ComplianceChip compliance={licence.compliance} />
                        <Chip label={humanise(licence.model)} size="small" variant="outlined" />
                      </Stack>

                      <Typography variant="caption" color="text.secondary">
                        {licence.publisher ? `${licence.publisher} · ` : ''}
                        {licence.deployedCount} of {licence.entitlementCount} used
                        {licence.overDeployedBy > 0
                          ? ` · ${licence.overDeployedBy} over entitlement`
                          : ''}
                        {licence.expiresOn ? ` · expires ${formatDate(licence.expiresOn)}` : ''}
                      </Typography>
                    </Box>

                    {licence.annualCost != null && (
                      <Typography variant="body2" fontWeight={600}>
                        {formatCurrency(licence.annualCost)}/yr
                      </Typography>
                    )}
                  </Stack>
                </CardContent>
              </Card>
            ))}
          </Stack>
        </>
      )}
    </Box>
  );
}
