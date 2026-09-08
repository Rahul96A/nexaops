import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  Box,
  Button,
  Card,
  CardContent,
  CardHeader,
  Chip,
  LinearProgress,
  Skeleton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import WhatshotOutlinedIcon from '@mui/icons-material/WhatshotOutlined';
import InboxOutlinedIcon from '@mui/icons-material/InboxOutlined';
import AssignmentIndOutlinedIcon from '@mui/icons-material/AssignmentIndOutlined';
import TimerOutlinedIcon from '@mui/icons-material/TimerOutlined';
import TaskAltOutlinedIcon from '@mui/icons-material/TaskAltOutlined';
import { incidentsApi } from '@/api/incidents';
import type { IncidentViewScope } from '@/api/types';
import { PageHeader } from '@/components/PageHeader';
import { ErrorState } from '@/components/ErrorState';
import { EmptyState } from '@/components/EmptyState';
import { StatCard } from '@/components/StatCard';
import { PriorityChip, SlaBadge, StatusChip, UserChip } from '@/components/StatusChips';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { useAuth } from '@/auth/useAuth';
import { formatRelative, humanise } from '@/utils/format';
import { priorityColors } from '@/theme/theme';

/**
 * The service desk dashboard.
 *
 * Every number here is a live aggregate from the API, computed over the records the signed-in
 * user is allowed to see. Nothing on this page is a fixed figure, which is why each tile is a
 * link into the queue that produced it: a manager who does not believe a count can click it and
 * read the rows.
 */
export function ServiceDeskPage() {
  const navigate = useNavigate();
  const { profile } = useAuth();

  const summary = useQuery({
    queryKey: ['incidents', 'summary'],
    queryFn: ({ signal }) => incidentsApi.summary(signal),
    // The queue moves while a manager is looking at it.
    refetchInterval: 60_000,
  });

  const attention = useQuery({
    queryKey: ['incidents', 'needs-attention'],
    queryFn: ({ signal }) =>
      incidentsApi.search(
        { scope: 'Breached', pageSize: 8, sortBy: 'priority', sortDirection: 'Ascending' },
        signal,
      ),
    refetchInterval: 60_000,
  });

  const recent = useQuery({
    queryKey: ['incidents', 'recent'],
    queryFn: ({ signal }) =>
      incidentsApi.search(
        { openOnly: true, pageSize: 8, sortBy: 'createdAt', sortDirection: 'Descending' },
        signal,
      ),
    refetchInterval: 60_000,
  });

  function goToQueue(scope: IncidentViewScope) {
    navigate(`/incidents?scope=${scope}`);
  }

  const firstName = profile?.firstName ?? 'there';

  return (
    <>
      <PageHeader
        title={`Good ${greeting()}, ${firstName}`}
        subtitle={
          summary.data
            ? `${summary.data.openIncidents} open · ${summary.data.createdToday} raised today · ${summary.data.resolvedToday} resolved today`
            : 'Loading the service desk…'
        }
        actions={
          <Can permission={Permissions.incidentCreate}>
            <Button variant="contained" startIcon={<AddIcon />} onClick={() => navigate('/incidents/new')}>
              Raise incident
            </Button>
          </Can>
        }
      />

      {summary.isError && <ErrorState error={summary.error} onRetry={() => summary.refetch()} />}

      {/* Counters */}
      <Box
        sx={{
          display: 'grid',
          gap: 2,
          gridTemplateColumns: {
            xs: 'repeat(2, 1fr)',
            sm: 'repeat(3, 1fr)',
            lg: 'repeat(6, 1fr)',
          },
          mb: 3,
        }}
      >
        {summary.isLoading
          ? Array.from({ length: 6 }, (_, index) => (
              <Skeleton key={index} variant="rounded" height={116} />
            ))
          : summary.data && (
              <>
                <StatCard
                  label="Open"
                  value={summary.data.openIncidents}
                  icon={<InboxOutlinedIcon />}
                  onClick={() => navigate('/incidents?openOnly=true')}
                />
                <StatCard
                  label="P1 critical"
                  value={summary.data.criticalOpen}
                  tone={summary.data.criticalOpen > 0 ? 'critical' : 'default'}
                  icon={<WhatshotOutlinedIcon />}
                  onClick={() => navigate('/incidents?priority=P1Critical&openOnly=true')}
                />
                <StatCard
                  label="P2 high"
                  value={summary.data.highOpen}
                  tone={summary.data.highOpen > 0 ? 'warning' : 'default'}
                  icon={<WhatshotOutlinedIcon />}
                  onClick={() => navigate('/incidents?priority=P2High&openOnly=true')}
                />
                <StatCard
                  label="SLA breached"
                  value={summary.data.breachedOpen}
                  tone={summary.data.breachedOpen > 0 ? 'critical' : 'success'}
                  icon={<ErrorOutlineIcon />}
                  onClick={() => goToQueue('Breached')}
                />
                <StatCard
                  label="Unassigned"
                  value={summary.data.unassignedInMyGroups}
                  caption="in my groups"
                  icon={<InboxOutlinedIcon />}
                  onClick={() => goToQueue('Unassigned')}
                />
                <StatCard
                  label="Assigned to me"
                  value={summary.data.assignedToMe}
                  icon={<AssignmentIndOutlinedIcon />}
                  onClick={() => goToQueue('AssignedToMe')}
                />
              </>
            )}
      </Box>

      <Box
        sx={{
          display: 'grid',
          gap: 2,
          gridTemplateColumns: { xs: '1fr', lg: '2fr 1fr' },
          alignItems: 'start',
        }}
      >
        <Stack spacing={2}>
          {/* Breached work first: it is the thing a service desk manager opens this page for. */}
          <Card>
            <CardHeader
              title="Needs attention"
              subheader="Open incidents that have breached an SLA commitment"
              titleTypographyProps={{ variant: 'h3' }}
              subheaderTypographyProps={{ variant: 'body2' }}
              action={
                <Button size="small" onClick={() => goToQueue('Breached')}>
                  View all
                </Button>
              }
            />

            {attention.isLoading && <LinearProgress />}

            <CardContent sx={{ pt: 0, px: 0 }}>
              {attention.isError && (
                <ErrorState error={attention.error} onRetry={() => attention.refetch()} />
              )}

              {attention.data && attention.data.items.length === 0 && (
                <EmptyState
                  title="Nothing has breached"
                  description="Every open incident is inside its SLA commitment."
                  icon={<TaskAltOutlinedIcon />}
                />
              )}

              {attention.data && attention.data.items.length > 0 && (
                <IncidentMiniTable
                  rows={attention.data.items}
                  onOpen={(id) => navigate(`/incidents/${id}`)}
                />
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader
              title="Latest activity"
              subheader="Most recently raised open incidents"
              titleTypographyProps={{ variant: 'h3' }}
              subheaderTypographyProps={{ variant: 'body2' }}
              action={
                <Button size="small" onClick={() => navigate('/incidents?openOnly=true')}>
                  View queue
                </Button>
              }
            />

            {recent.isLoading && <LinearProgress />}

            <CardContent sx={{ pt: 0, px: 0 }}>
              {recent.isError && <ErrorState error={recent.error} onRetry={() => recent.refetch()} />}

              {recent.data && recent.data.items.length === 0 && (
                <EmptyState title="No open incidents" description="The queue is clear." />
              )}

              {recent.data && recent.data.items.length > 0 && (
                <IncidentMiniTable
                  rows={recent.data.items}
                  onOpen={(id) => navigate(`/incidents/${id}`)}
                />
              )}
            </CardContent>
          </Card>
        </Stack>

        <Stack spacing={2}>
          <Card>
            <CardHeader
              title="Open by priority"
              titleTypographyProps={{ variant: 'h3' }}
            />
            <CardContent sx={{ pt: 0 }}>
              {summary.isLoading && <Skeleton variant="rounded" height={140} />}

              {summary.data && summary.data.openByPriority.length === 0 && (
                <Typography variant="body2" color="text.secondary">
                  No open incidents.
                </Typography>
              )}

              {summary.data && summary.data.openByPriority.length > 0 && (
                <Stack spacing={1.5}>
                  {summary.data.openByPriority.map((row) => {
                    const total = summary.data.openIncidents || 1;
                    const share = Math.round((row.count / total) * 100);

                    return (
                      <Box key={row.priority}>
                        <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
                          <Typography variant="body2">{priorityColors[row.priority].label}</Typography>
                          <Typography variant="body2" sx={{ fontWeight: 700 }}>
                            {row.count}
                          </Typography>
                        </Box>

                        <LinearProgress
                          variant="determinate"
                          value={share}
                          aria-label={`${priorityColors[row.priority].label}: ${row.count} incidents`}
                          sx={{
                            height: 6,
                            borderRadius: 3,
                            backgroundColor: 'action.hover',
                            '& .MuiLinearProgress-bar': {
                              backgroundColor: priorityColors[row.priority].main,
                              borderRadius: 3,
                            },
                          }}
                        />
                      </Box>
                    );
                  })}
                </Stack>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader title="Open by status" titleTypographyProps={{ variant: 'h3' }} />
            <CardContent sx={{ pt: 0 }}>
              {summary.isLoading && <Skeleton variant="rounded" height={100} />}

              {summary.data && (
                <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                  {summary.data.openByStatus.map((row) => (
                    <Chip
                      key={row.status}
                      label={`${humanise(row.status)} · ${row.count}`}
                      variant="outlined"
                      onClick={() => navigate(`/incidents?status=${row.status}`)}
                    />
                  ))}
                </Stack>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader
              title="Team workload"
              subheader="Open work per agent in your groups"
              titleTypographyProps={{ variant: 'h3' }}
              subheaderTypographyProps={{ variant: 'body2' }}
            />
            <CardContent sx={{ pt: 0 }}>
              {summary.isLoading && <Skeleton variant="rounded" height={160} />}

              {summary.data && summary.data.teamWorkload.length === 0 && (
                <Typography variant="body2" color="text.secondary">
                  No assigned work in your groups.
                </Typography>
              )}

              {summary.data && summary.data.teamWorkload.length > 0 && (
                <Stack spacing={1.5}>
                  {summary.data.teamWorkload.map((agent) => (
                    <Box
                      key={agent.userId}
                      sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1 }}
                    >
                      <UserChip name={agent.displayName} color={agent.avatarColor} />

                      <Stack direction="row" spacing={0.5} alignItems="center">
                        {agent.breachedCount > 0 && (
                          <Chip
                            label={`${agent.breachedCount} breached`}
                            size="small"
                            sx={{ bgcolor: '#FDE7E7', color: '#C62828', fontWeight: 700 }}
                          />
                        )}
                        <Chip label={agent.openCount} size="small" variant="outlined" />
                      </Stack>
                    </Box>
                  ))}
                </Stack>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader title="Today" titleTypographyProps={{ variant: 'h3' }} />
            <CardContent sx={{ pt: 0 }}>
              {summary.data && (
                <Stack spacing={1.5}>
                  <MetricRow
                    icon={<InboxOutlinedIcon fontSize="small" />}
                    label="Raised"
                    value={summary.data.createdToday}
                  />
                  <MetricRow
                    icon={<TaskAltOutlinedIcon fontSize="small" />}
                    label="Resolved"
                    value={summary.data.resolvedToday}
                  />
                  <MetricRow
                    icon={<TimerOutlinedIcon fontSize="small" />}
                    label="Due within 2 hours"
                    value={summary.data.dueWithinTwoHours}
                  />
                </Stack>
              )}
            </CardContent>
          </Card>
        </Stack>
      </Box>
    </>
  );
}

function MetricRow({ icon, label, value }: { icon: React.ReactNode; label: string; value: number }) {
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, color: 'text.secondary' }}>
        {icon}
        <Typography variant="body2">{label}</Typography>
      </Box>
      <Typography variant="h4">{value}</Typography>
    </Box>
  );
}

function IncidentMiniTable({
  rows,
  onOpen,
}: {
  rows: import('@/api/types').IncidentListItem[];
  onOpen: (id: string) => void;
}) {
  return (
    <Box sx={{ overflowX: 'auto' }}>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>Number</TableCell>
            <TableCell>Summary</TableCell>
            <TableCell>Priority</TableCell>
            <TableCell>Status</TableCell>
            <TableCell>Assignee</TableCell>
            <TableCell>SLA</TableCell>
            <TableCell>Raised</TableCell>
          </TableRow>
        </TableHead>

        <TableBody>
          {rows.map((incident) => (
            <TableRow
              key={incident.id}
              hover
              onClick={() => onOpen(incident.id)}
              sx={{ cursor: 'pointer' }}
            >
              <TableCell sx={{ fontFamily: 'monospace', fontSize: '0.8125rem', whiteSpace: 'nowrap' }}>
                {incident.number}
              </TableCell>

              <TableCell sx={{ maxWidth: 320 }}>
                <Typography variant="body2" noWrap title={incident.title}>
                  {incident.title}
                </Typography>
              </TableCell>

              <TableCell>
                <PriorityChip priority={incident.priority} />
              </TableCell>

              <TableCell>
                <StatusChip status={incident.status} />
              </TableCell>

              <TableCell>
                <UserChip name={incident.assignedToName} color={null} size={22} />
              </TableCell>

              <TableCell>
                <SlaBadge breached={incident.hasBreachedSla} dueAt={incident.nextSlaDueAt} />
              </TableCell>

              <TableCell sx={{ whiteSpace: 'nowrap' }}>
                <Typography variant="caption" color="text.secondary">
                  {formatRelative(incident.createdAt)}
                </Typography>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Box>
  );
}

function greeting(): string {
  const hour = new Date().getHours();

  if (hour < 12) {
    return 'morning';
  }

  if (hour < 17) {
    return 'afternoon';
  }

  return 'evening';
}
