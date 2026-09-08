import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  Box,
  Card,
  CardHeader,
  LinearProgress,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import TaskAltOutlinedIcon from '@mui/icons-material/TaskAltOutlined';
import { incidentsApi } from '@/api/incidents';
import type { IncidentSearchParams } from '@/api/types';
import { PageHeader } from '@/components/PageHeader';
import { ErrorState } from '@/components/ErrorState';
import { EmptyState } from '@/components/EmptyState';
import { PriorityChip, SlaBadge, StatusChip } from '@/components/StatusChips';
import { useAuth } from '@/auth/useAuth';
import { formatRelative } from '@/utils/format';

/**
 * The personal queue.
 *
 * Three lists rather than one filtered table, because "what is mine", "what is my team's" and
 * "what have I asked for" are different questions an agent asks at different moments.
 */
export function MyWorkPage() {
  const { profile } = useAuth();

  return (
    <>
      <PageHeader
        title="My work"
        subtitle={`Everything assigned to you or your groups in ${profile?.tenantName ?? 'your organization'}`}
      />

      <Stack spacing={2}>
        <WorkList
          title="Assigned to me"
          subtitle="Open incidents you own"
          params={{ scope: 'AssignedToMe', pageSize: 25, sortBy: 'nextSlaDueAt', sortDirection: 'Ascending' }}
          emptyTitle="Nothing assigned to you"
          emptyDescription="Work assigned to you appears here, soonest SLA deadline first."
        />

        <WorkList
          title="Unassigned in my groups"
          subtitle="Waiting for someone to pick up"
          params={{ scope: 'Unassigned', pageSize: 25, sortBy: 'createdAt', sortDirection: 'Ascending' }}
          emptyTitle="Nothing waiting"
          emptyDescription="Every incident in your groups has an owner."
        />

        <WorkList
          title="Raised by me"
          subtitle="Incidents you reported"
          params={{ scope: 'RaisedByMe', pageSize: 10, sortBy: 'createdAt', sortDirection: 'Descending' }}
          emptyTitle="You have not raised any incidents"
        />
      </Stack>
    </>
  );
}

function WorkList({
  title,
  subtitle,
  params,
  emptyTitle,
  emptyDescription,
}: {
  title: string;
  subtitle: string;
  params: IncidentSearchParams;
  emptyTitle: string;
  emptyDescription?: string;
}) {
  const navigate = useNavigate();

  const incidents = useQuery({
    queryKey: ['incidents', 'my-work', params],
    queryFn: ({ signal }) => incidentsApi.search(params, signal),
    refetchInterval: 60_000,
  });

  return (
    <Card>
      <CardHeader
        title={title}
        subheader={subtitle}
        titleTypographyProps={{ variant: 'h3' }}
        subheaderTypographyProps={{ variant: 'body2' }}
        action={
          incidents.data && (
            <Typography variant="h3" color="text.secondary" sx={{ pr: 1 }}>
              {incidents.data.totalCount}
            </Typography>
          )
        }
      />

      {incidents.isLoading && <LinearProgress />}

      {incidents.isError && <ErrorState error={incidents.error} onRetry={() => incidents.refetch()} />}

      {incidents.data && incidents.data.items.length === 0 && (
        <EmptyState title={emptyTitle} description={emptyDescription} icon={<TaskAltOutlinedIcon />} />
      )}

      {incidents.data && incidents.data.items.length > 0 && (
        <Box sx={{ overflowX: 'auto' }}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Number</TableCell>
                <TableCell>Summary</TableCell>
                <TableCell>Priority</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>SLA</TableCell>
                <TableCell>Raised</TableCell>
              </TableRow>
            </TableHead>

            <TableBody>
              {incidents.data.items.map((incident) => (
                <TableRow
                  key={incident.id}
                  hover
                  onClick={() => navigate(`/incidents/${incident.id}`)}
                  sx={{ cursor: 'pointer' }}
                >
                  <TableCell sx={{ fontFamily: 'monospace', fontSize: '0.8125rem', whiteSpace: 'nowrap' }}>
                    {incident.number}
                  </TableCell>

                  <TableCell sx={{ maxWidth: 420 }}>
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
      )}
    </Card>
  );
}
