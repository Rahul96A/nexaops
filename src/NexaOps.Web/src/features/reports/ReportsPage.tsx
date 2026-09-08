import { useCallback, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CardHeader,
  Chip,
  LinearProgress,
  MenuItem,
  Stack,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tabs,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import DownloadOutlinedIcon from '@mui/icons-material/DownloadOutlined';
import { reportsApi } from '@/api/reports';
import { downloadFile } from '@/api/client';
import type { BreakdownRow, DurationStats, Rate } from '@/api/types';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { PageHeader } from '@/components/PageHeader';
import { ErrorState } from '@/components/ErrorState';
import { StatCard } from '@/components/StatCard';
import { TrendChart } from '@/components/TrendChart';
import { formatDate, humanise } from '@/utils/format';

type ReportTab = 'service-desk' | 'sla' | 'changes' | 'requests';

const presets = [
  { days: 6, label: 'Last 7 days' },
  { days: 29, label: 'Last 30 days' },
  { days: 89, label: 'Last 90 days' },
];

function isoDaysAgo(days: number): string {
  const date = new Date();
  date.setUTCDate(date.getUTCDate() - days);
  return date.toISOString().slice(0, 10);
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

/**
 * Operational reporting.
 *
 * Every figure comes from the records the tenant holds. Where a figure cannot be computed — no
 * resolutions in the window, a rate with no denominator — the page says so rather than showing a
 * zero, because zero is a measurement and "we cannot say" is not.
 */
export function ReportsPage() {
  const [params, setParams] = useSearchParams();
  const [downloadError, setDownloadError] = useState<string | null>(null);

  const tab = (params.get('tab') as ReportTab) ?? 'service-desk';
  const from = params.get('from') ?? isoDaysAgo(29);
  const to = params.get('to') ?? today();

  const period = { from, to };

  const update = useCallback(
    (changes: Record<string, string | null>) => {
      const next = new URLSearchParams(params);

      for (const [key, value] of Object.entries(changes)) {
        if (value === null) {
          next.delete(key);
        } else {
          next.set(key, value);
        }
      }

      setParams(next, { replace: true });
    },
    [params, setParams],
  );

  const serviceDesk = useQuery({
    queryKey: ['report', 'service-desk', from, to],
    queryFn: ({ signal }) => reportsApi.serviceDesk(period, signal),
    enabled: tab === 'service-desk',
  });

  const sla = useQuery({
    queryKey: ['report', 'sla', from, to],
    queryFn: ({ signal }) => reportsApi.sla(period, signal),
    enabled: tab === 'sla',
  });

  const changes = useQuery({
    queryKey: ['report', 'changes', from, to],
    queryFn: ({ signal }) => reportsApi.changes(period, signal),
    enabled: tab === 'changes',
  });

  const requests = useQuery({
    queryKey: ['report', 'requests', from, to],
    queryFn: ({ signal }) => reportsApi.requests(period, signal),
    enabled: tab === 'requests',
  });

  const active = { 'service-desk': serviceDesk, sla, changes, requests }[tab];

  async function handleExport() {
    setDownloadError(null);

    try {
      await downloadFile(`/reports/${tab}/export`, period);
    } catch (error) {
      setDownloadError(error instanceof Error ? error.message : 'The export could not be downloaded.');
    }
  }

  return (
    <Box>
      <PageHeader
        title="Reports"
        subtitle="Measured from the records themselves. Nothing here is cached or estimated."
        actions={
          <Can permission={Permissions.reportExport}>
            <Button startIcon={<DownloadOutlinedIcon />} onClick={() => void handleExport()}>
              Export CSV
            </Button>
          </Can>
        }
      />

      {downloadError && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setDownloadError(null)}>
          {downloadError}
        </Alert>
      )}

      <Stack direction={{ xs: 'column', md: 'row' }} gap={2} sx={{ mb: 2 }} alignItems="center">
        <TextField
          select
          size="small"
          label="Period"
          value={presets.find((p) => isoDaysAgo(p.days) === from)?.days ?? ''}
          onChange={(event) =>
            update({ from: isoDaysAgo(Number(event.target.value)), to: today() })
          }
          sx={{ minWidth: 170 }}
        >
          {presets.map((preset) => (
            <MenuItem key={preset.days} value={preset.days}>
              {preset.label}
            </MenuItem>
          ))}
        </TextField>

        <TextField
          type="date"
          size="small"
          label="From"
          value={from}
          onChange={(event) => update({ from: event.target.value })}
          slotProps={{ inputLabel: { shrink: true } }}
        />

        <TextField
          type="date"
          size="small"
          label="To"
          value={to}
          onChange={(event) => update({ to: event.target.value })}
          slotProps={{ inputLabel: { shrink: true } }}
        />
      </Stack>

      <Tabs
        value={tab}
        onChange={(_, value: ReportTab) => update({ tab: value })}
        sx={{ mb: 2 }}
        variant="scrollable"
        allowScrollButtonsMobile
      >
        <Tab label="Service desk" value="service-desk" />
        <Tab label="SLA attainment" value="sla" />
        <Tab label="Changes" value="changes" />
        <Tab label="Requests" value="requests" />
      </Tabs>

      {active.isLoading && <LinearProgress />}
      {active.error && <ErrorState error={active.error} onRetry={() => void active.refetch()} />}

      {tab === 'service-desk' && serviceDesk.data && (
        <Stack gap={3}>
          {serviceDesk.data.isPartialPeriod && (
            <Alert severity="info">
              This period ends today, which is not a whole day. The last point on the chart will
              rise as the day goes on.
            </Alert>
          )}

          <Stack direction="row" gap={2} flexWrap="wrap">
            <StatCard label="Raised" value={serviceDesk.data.created} />
            <StatCard label="Resolved" value={serviceDesk.data.resolved} />
            <StatCard label="Open now" value={serviceDesk.data.stillOpen} />
            <StatCard
              label="Within SLA"
              value={formatRate(serviceDesk.data.slaAttainment)}
              tone={toneForAttainment(serviceDesk.data.slaAttainment)}
            />
            <StatCard label="Reopened" value={formatRate(serviceDesk.data.reopenRate)} />
          </Stack>

          <Card variant="outlined">
            <CardHeader
              title="Raised against resolved"
              titleTypographyProps={{ variant: 'h4' }}
              subheader="Where the lines diverge, the backlog is moving."
            />
            <CardContent>
              <TrendChart
                points={serviceDesk.data.daily}
                label={`Incidents raised and resolved, ${formatDate(from)} to ${formatDate(to)}`}
              />
            </CardContent>
          </Card>

          <Card variant="outlined">
            <CardHeader
              title="Time to resolve"
              titleTypographyProps={{ variant: 'h4' }}
              subheader="Mean and median together. Where they disagree sharply, the difference is the finding."
            />
            <CardContent>
              <DurationSummary stats={serviceDesk.data.timeToResolve} />
            </CardContent>
          </Card>

          <BreakdownCard
            title="By priority"
            subheader="Raised in the period, and how many of those breached."
            rows={serviceDesk.data.byPriority}
          />

          <BreakdownCard
            title="Where the work comes from"
            subheader="The fifteen busiest categories."
            rows={serviceDesk.data.byCategory}
          />

          <BreakdownCard
            title="By assignment group"
            subheader="The fifteen busiest groups."
            rows={serviceDesk.data.byGroup}
          />
        </Stack>
      )}

      {tab === 'sla' && sla.data && (
        <Stack gap={3}>
          <Alert severity="info">
            Counted from commitments that finished in this period. {sla.data.stillRunning} clock
            {sla.data.stillRunning === 1 ? ' is' : 's are'} still running and {sla.data.cancelled}{' '}
            {sla.data.cancelled === 1 ? 'was' : 'were'} withdrawn — neither is evidence either way,
            so neither is counted.
          </Alert>

          <Stack direction="row" gap={2} flexWrap="wrap">
            <StatCard
              label="Attainment"
              value={formatRate(sla.data.overall)}
              tone={toneForAttainment(sla.data.overall)}
            />
            <StatCard label="Met" value={sla.data.overall.numerator} />
            <StatCard
              label="Breached"
              value={sla.data.overall.denominator - sla.data.overall.numerator}
              tone="critical"
            />
          </Stack>

          <Card variant="outlined">
            <TableContainer>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Commitment</TableCell>
                    <TableCell align="right">Met</TableCell>
                    <TableCell align="right">Breached</TableCell>
                    <TableCell align="right">Attainment</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {sla.data.rows.map((row) => (
                    <TableRow key={row.target}>
                      <TableCell>{humanise(row.target)}</TableCell>
                      <TableCell align="right">{row.met}</TableCell>
                      <TableCell align="right">{row.breached}</TableCell>
                      <TableCell align="right">{formatRate(row.attainment)}</TableCell>
                    </TableRow>
                  ))}

                  {sla.data.rows.length === 0 && (
                    <TableRow>
                      <TableCell colSpan={4}>
                        <Typography variant="body2" color="text.secondary">
                          No commitments finished in this period.
                        </Typography>
                      </TableCell>
                    </TableRow>
                  )}
                </TableBody>
              </Table>
            </TableContainer>
          </Card>
        </Stack>
      )}

      {tab === 'changes' && changes.data && (
        <Stack gap={3}>
          <Stack direction="row" gap={2} flexWrap="wrap">
            <StatCard label="Raised" value={changes.data.raised} />
            <StatCard label="Reviewed" value={changes.data.reviewed} />
            <StatCard
              label="Awaiting review"
              value={changes.data.awaitingReview}
              tone={changes.data.awaitingReview > 0 ? 'warning' : 'default'}
            />
            <StatCard
              label="Successful"
              value={formatRate(changes.data.successRate)}
              tone={toneForAttainment(changes.data.successRate)}
            />
            <StatCard label="Emergency" value={formatRate(changes.data.emergencyShare)} />
          </Stack>

          <Alert severity="info">
            Outcomes are recorded at review, so a change nobody has reviewed yet is not counted
            either way. &ldquo;Successful with issues&rdquo; is kept out of the headline success
            rate on purpose: an overrun or an unplanned side effect is exactly what the change
            process exists to reduce.
          </Alert>

          <Card variant="outlined">
            <CardHeader title="Outcomes" titleTypographyProps={{ variant: 'h4' }} />
            <CardContent>
              {changes.data.outcomes.length === 0 && (
                <Typography variant="body2" color="text.secondary">
                  No changes were reviewed in this period.
                </Typography>
              )}

              <Stack gap={1}>
                {changes.data.outcomes.map((row) => (
                  <Stack key={row.outcome} direction="row" gap={2} alignItems="center">
                    <Chip
                      label={humanise(row.outcome)}
                      size="small"
                      color={row.outcome === 'Successful' ? 'success' : 'warning'}
                      variant={row.outcome === 'Successful' ? 'filled' : 'outlined'}
                    />
                    <Typography variant="body2">{row.count}</Typography>
                  </Stack>
                ))}
              </Stack>
            </CardContent>
          </Card>
        </Stack>
      )}

      {tab === 'requests' && requests.data && (
        <Stack gap={3}>
          <Stack direction="row" gap={2} flexWrap="wrap">
            <StatCard label="Raised" value={requests.data.raised} />
            <StatCard label="Fulfilled" value={requests.data.fulfilled} />
            <StatCard
              label="Awaiting approval"
              value={requests.data.awaitingApproval}
              tone={requests.data.awaitingApproval > 0 ? 'warning' : 'default'}
            />
            <StatCard label="Cancelled" value={requests.data.cancelled} />
          </Stack>

          <Card variant="outlined">
            <CardHeader
              title="Raised against fulfilled"
              titleTypographyProps={{ variant: 'h4' }}
            />
            <CardContent>
              <TrendChart
                points={requests.data.daily}
                label={`Requests raised and fulfilled, ${formatDate(from)} to ${formatDate(to)}`}
              />
            </CardContent>
          </Card>

          <Card variant="outlined">
            <CardHeader title="Time to fulfil" titleTypographyProps={{ variant: 'h4' }} />
            <CardContent>
              <DurationSummary stats={requests.data.timeToFulfil} />
            </CardContent>
          </Card>

          <BreakdownCard
            title="What the catalogue is generating"
            subheader="The fifteen most-ordered items."
            rows={requests.data.byCatalogItem}
            hideBreach
          />
        </Stack>
      )}
    </Box>
  );
}

function BreakdownCard({
  title,
  subheader,
  rows,
  hideBreach = false,
}: {
  title: string;
  subheader: string;
  rows: BreakdownRow[];
  hideBreach?: boolean;
}) {
  return (
    <Card variant="outlined">
      <CardHeader
        title={title}
        subheader={subheader}
        titleTypographyProps={{ variant: 'h4' }}
      />
      <TableContainer>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{hideBreach ? 'Item' : 'Group'}</TableCell>
              <TableCell align="right">Raised</TableCell>
              <TableCell align="right">{hideBreach ? 'Delivered' : 'Resolved'}</TableCell>
              {!hideBreach && <TableCell align="right">Breached</TableCell>}
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={`${row.label}-${row.id ?? ''}`}>
                <TableCell>{row.label}</TableCell>
                <TableCell align="right">{row.created}</TableCell>
                <TableCell align="right">{row.resolved}</TableCell>
                {!hideBreach && (
                  <TableCell align="right">
                    <Tooltip title={`${row.breached} of ${row.created}`}>
                      <span>{formatRate(row.breachRate)}</span>
                    </Tooltip>
                  </TableCell>
                )}
              </TableRow>
            ))}

            {rows.length === 0 && (
              <TableRow>
                <TableCell colSpan={hideBreach ? 3 : 4}>
                  <Typography variant="body2" color="text.secondary">
                    Nothing in this period.
                  </Typography>
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </TableContainer>
    </Card>
  );
}

function DurationSummary({ stats }: { stats: DurationStats }) {
  if (stats.sample === 0) {
    return (
      <Typography variant="body2" color="text.secondary">
        Nothing was resolved in this period, so there is no average to report. That is not the
        same as an average of zero.
      </Typography>
    );
  }

  return (
    <Stack direction="row" gap={4} flexWrap="wrap">
      <Box>
        <Typography variant="h3">{formatDuration(stats.mean)}</Typography>
        <Typography variant="caption" color="text.secondary">
          Mean
        </Typography>
      </Box>
      <Box>
        <Typography variant="h3">{formatDuration(stats.median)}</Typography>
        <Typography variant="caption" color="text.secondary">
          Median
        </Typography>
      </Box>
      <Box>
        <Typography variant="h3">{stats.sample}</Typography>
        <Typography variant="caption" color="text.secondary">
          Resolved in the period
        </Typography>
      </Box>
    </Stack>
  );
}

/** A rate as text, with an em dash where there is no denominator to divide by. */
function formatRate(rate: Rate): string {
  return rate.percent == null ? '—' : `${rate.percent}%`;
}

function toneForAttainment(rate: Rate): 'default' | 'critical' | 'warning' | 'success' {
  if (rate.percent == null) {
    return 'default';
  }

  if (rate.percent >= 95) {
    return 'success';
  }

  return rate.percent >= 85 ? 'warning' : 'critical';
}

/**
 * A .NET TimeSpan ("1.02:03:04" or "02:03:04") as something readable.
 *
 * Rendered in hours and minutes rather than days: a service desk thinks in hours, and "1.2 days"
 * invites the question of whether that counts the night.
 */
function formatDuration(value?: string | null): string {
  if (!value) {
    return '—';
  }

  const match = /^(?:(\d+)\.)?(\d{2}):(\d{2}):/.exec(value);

  if (!match) {
    return value;
  }

  const days = Number(match[1] ?? 0);
  const hours = Number(match[2]) + days * 24;
  const minutes = Number(match[3]);

  return hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m`;
}
