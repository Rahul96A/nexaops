import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CardHeader,
  Chip,
  Divider,
  LinearProgress,
  Skeleton,
  Stack,
  Tab,
  Tabs,
  Typography,
} from '@mui/material';
import WhatshotOutlinedIcon from '@mui/icons-material/WhatshotOutlined';
import { incidentsApi } from '@/api/incidents';
import { auditApi } from '@/api/notifications';
import type { IncidentDetail } from '@/api/types';
import { PageHeader } from '@/components/PageHeader';
import { ErrorState } from '@/components/ErrorState';
import { PriorityChip, SlaMeter, StatusChip, UserChip } from '@/components/StatusChips';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { useAuth } from '@/auth/useAuth';
import { formatDateTime, formatLongDateTime, formatRelative, humanise } from '@/utils/format';
import { IncidentActivityTab } from './components/IncidentActivityTab';
import { IncidentActionBar } from './components/IncidentActionBar';
import { IncidentAuditTab } from './components/IncidentAuditTab';

/**
 * One incident, in full.
 *
 * The layout puts the conversation in the main column and the record's facts in a sidebar,
 * because an agent working a ticket spends their time reading and writing the conversation and
 * only glances at the metadata.
 */
export function IncidentDetailPage() {
  const { id = '' } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { hasPermission } = useAuth();

  const [tab, setTab] = useState(0);

  const incident = useQuery({
    queryKey: ['incidents', 'detail', id],
    queryFn: ({ signal }) => incidentsApi.get(id, signal),
    enabled: Boolean(id),
  });

  const audit = useQuery({
    queryKey: ['audit', 'Incident', id],
    queryFn: ({ signal }) => auditApi.forRecord('Incident', id, signal),
    enabled: Boolean(id) && hasPermission(Permissions.auditRead) && tab === 2,
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['incidents'] });
    void queryClient.invalidateQueries({ queryKey: ['audit', 'Incident', id] });
  };

  const declareMajor = useMutation({
    mutationFn: (payload: { isMajor: boolean; reason: string }) =>
      incidentsApi.declareMajor(id, payload.isMajor, payload.reason),
    onSuccess: invalidate,
  });

  if (incident.isLoading) {
    return <IncidentDetailSkeleton />;
  }

  if (incident.isError) {
    return (
      <>
        <PageHeader title="Incident" crumbs={[{ label: 'Incidents', to: '/incidents' }]} />
        <ErrorState error={incident.error} onRetry={() => incident.refetch()} />
      </>
    );
  }

  const record = incident.data;
  if (!record) {
    return null;
  }

  return (
    <>
      <PageHeader
        crumbs={[{ label: 'Incidents', to: '/incidents' }, { label: record.number }]}
        title={
          <Stack direction="row" spacing={1.5} alignItems="center" flexWrap="wrap" useFlexGap>
            <Typography component="span" variant="h1" sx={{ fontFamily: 'monospace' }}>
              {record.number}
            </Typography>

            {record.isMajorIncident && (
              <Chip
                icon={<WhatshotOutlinedIcon />}
                label="Major incident"
                color="error"
                sx={{ fontWeight: 700 }}
              />
            )}
          </Stack>
        }
        subtitle={record.title}
        actions={
          <IncidentActionBar
            incident={record}
            onChanged={invalidate}
            onDeclareMajor={(isMajor, reason) => declareMajor.mutate({ isMajor, reason })}
          />
        }
      />

      {record.status === 'Resolved' && (
        <Alert severity="success" sx={{ mb: 2 }}>
          Resolved {formatRelative(record.resolvedAt)} by {record.resolvedByName ?? 'unknown'} —{' '}
          {record.resolutionNotes}
        </Alert>
      )}

      {record.reopenCount > 0 && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          This incident has been reopened {record.reopenCount}{' '}
          {record.reopenCount === 1 ? 'time' : 'times'}.
        </Alert>
      )}

      <Box
        sx={{
          display: 'grid',
          gap: 2,
          gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 1fr) 360px' },
          alignItems: 'start',
        }}
      >
        <Card>
          <Tabs
            value={tab}
            onChange={(_, value: number) => setTab(value)}
            sx={{ px: 2, borderBottom: 1, borderColor: 'divider' }}
          >
            <Tab label="Activity" />
            <Tab label="Details" />
            <Tab
              label="Audit history"
              disabled={!hasPermission(Permissions.auditRead)}
            />
          </Tabs>

          {tab === 0 && <IncidentActivityTab incident={record} onChanged={invalidate} />}

          {tab === 1 && <IncidentDetailsTab incident={record} />}

          {tab === 2 && (
            <IncidentAuditTab
              events={audit.data ?? []}
              isLoading={audit.isLoading}
              error={audit.error}
            />
          )}
        </Card>

        <Stack spacing={2}>
          {/* SLA first: it is the thing that decides what an agent does next. */}
          <Card>
            <CardHeader title="Service level" titleTypographyProps={{ variant: 'h4' }} />
            <CardContent sx={{ pt: 0 }}>
              {record.slaInstances.length === 0 ? (
                <Typography variant="body2" color="text.secondary">
                  No SLA policy matches this incident.
                </Typography>
              ) : (
                <Stack spacing={2.5}>
                  {record.slaInstances.map((sla) => (
                    <SlaMeter key={sla.id} sla={sla} />
                  ))}
                </Stack>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader title="Assignment" titleTypographyProps={{ variant: 'h4' }} />
            <CardContent sx={{ pt: 0 }}>
              <Stack spacing={2}>
                <Field label="Group">
                  <Typography variant="body2">{record.assignmentGroupName ?? 'Unassigned'}</Typography>
                </Field>

                <Field label="Assignee">
                  <UserChip name={record.assignedToName} />
                </Field>

                <Field label="Requester">
                  <UserChip name={record.requesterName} />
                  {record.requesterEmail && (
                    <Typography variant="caption" color="text.secondary" sx={{ ml: 4.5 }}>
                      {record.requesterEmail}
                    </Typography>
                  )}
                </Field>

                {record.affectedUserName && record.affectedUserName !== record.requesterName && (
                  <Field label="Affected user">
                    <UserChip name={record.affectedUserName} />
                  </Field>
                )}
              </Stack>
            </CardContent>
          </Card>

          <Card>
            <CardHeader title="Classification" titleTypographyProps={{ variant: 'h4' }} />
            <CardContent sx={{ pt: 0 }}>
              <Stack spacing={2}>
                <Field label="Status">
                  <StatusChip status={record.status} size="medium" />
                  {record.pendingReason && (
                    <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.5 }}>
                      {humanise(record.pendingReason)}
                    </Typography>
                  )}
                </Field>

                <Field label="Priority">
                  <Stack direction="row" spacing={1} alignItems="center">
                    <PriorityChip priority={record.priority} size="medium" />
                    {record.isPriorityOverridden && (
                      <Chip label="Overridden" size="small" variant="outlined" color="warning" />
                    )}
                  </Stack>

                  {record.isPriorityOverridden && record.priorityOverrideReason && (
                    <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.5 }}>
                      {record.priorityOverrideReason}
                    </Typography>
                  )}

                  <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.5 }}>
                    Impact {humanise(record.impact)} · Urgency {humanise(record.urgency)}
                  </Typography>
                </Field>

                <Field label="Category">
                  <Typography variant="body2">
                    {record.categoryName ?? 'Unclassified'}
                    {record.subcategoryName ? ` · ${record.subcategoryName}` : ''}
                  </Typography>
                </Field>

                {record.tags.length > 0 && (
                  <Field label="Tags">
                    <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
                      {record.tags.map((tag) => (
                        <Chip
                          key={tag}
                          label={tag}
                          size="small"
                          variant="outlined"
                          onClick={() => navigate(`/incidents?tag=${encodeURIComponent(tag)}`)}
                        />
                      ))}
                    </Stack>
                  </Field>
                )}
              </Stack>
            </CardContent>
          </Card>

          <Card>
            <CardHeader title="Timestamps" titleTypographyProps={{ variant: 'h4' }} />
            <CardContent sx={{ pt: 0 }}>
              <Stack spacing={1.5}>
                <TimestampRow label="Raised" value={record.createdAt} by={record.createdByName} />
                <TimestampRow label="First response" value={record.firstRespondedAt} />
                <TimestampRow label="Resolved" value={record.resolvedAt} by={record.resolvedByName} />
                <TimestampRow label="Closed" value={record.closedAt} />
                <Divider />
                <TimestampRow label="Last updated" value={record.updatedAt} by={record.updatedByName} />
              </Stack>
            </CardContent>
          </Card>

          <Can permission={Permissions.incidentDeclareMajor}>
            <Card>
              <CardHeader title="Escalation" titleTypographyProps={{ variant: 'h4' }} />
              <CardContent sx={{ pt: 0 }}>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
                  {record.isMajorIncident
                    ? 'This incident is declared as a major incident.'
                    : 'Declaring a major incident is recorded in the audit trail with your justification.'}
                </Typography>

                <Button
                  variant={record.isMajorIncident ? 'outlined' : 'contained'}
                  color="error"
                  size="small"
                  fullWidth
                  disabled={declareMajor.isPending || record.status === 'Closed'}
                  onClick={() => {
                    const reason = window.prompt(
                      record.isMajorIncident
                        ? 'Why is this no longer a major incident?'
                        : 'Why is this a major incident?',
                    );

                    if (reason && reason.trim().length >= 10) {
                      declareMajor.mutate({ isMajor: !record.isMajorIncident, reason: reason.trim() });
                    }
                  }}
                >
                  {record.isMajorIncident ? 'Withdraw major incident' : 'Declare major incident'}
                </Button>
              </CardContent>
            </Card>
          </Can>
        </Stack>
      </Box>
    </>
  );
}

function IncidentDetailsTab({ incident }: { incident: IncidentDetail }) {
  return (
    <CardContent>
      <Typography variant="h4" gutterBottom>
        Description
      </Typography>

      <Typography
        variant="body1"
        sx={{ whiteSpace: 'pre-wrap', mb: 4, color: 'text.primary', lineHeight: 1.7 }}
      >
        {incident.description}
      </Typography>

      {incident.resolutionNotes && (
        <>
          <Typography variant="h4" gutterBottom>
            Resolution
          </Typography>

          <Chip
            label={humanise(incident.resolutionCode ?? '')}
            size="small"
            color="success"
            sx={{ mb: 1 }}
          />

          <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.7 }}>
            {incident.resolutionNotes}
          </Typography>
        </>
      )}
    </CardContent>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Box>
      <Typography
        variant="caption"
        sx={{ textTransform: 'uppercase', letterSpacing: '0.06em', fontWeight: 700 }}
        color="text.secondary"
        display="block"
        gutterBottom
      >
        {label}
      </Typography>
      {children}
    </Box>
  );
}

function TimestampRow({
  label,
  value,
  by,
}: {
  label: string;
  value?: string | null;
  by?: string | null;
}) {
  return (
    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 2 }}>
      <Typography variant="body2" color="text.secondary">
        {label}
      </Typography>

      <Box sx={{ textAlign: 'right', minWidth: 0 }}>
        <Typography variant="body2" noWrap title={value ? formatLongDateTime(value) : undefined}>
          {value ? formatDateTime(value) : '—'}
        </Typography>
        {by && (
          <Typography variant="caption" color="text.secondary" noWrap>
            {by}
          </Typography>
        )}
      </Box>
    </Box>
  );
}

function IncidentDetailSkeleton() {
  return (
    <>
      <Skeleton variant="text" width={220} height={44} />
      <Skeleton variant="text" width={480} sx={{ mb: 3 }} />

      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 1fr) 360px' } }}>
        <Skeleton variant="rounded" height={480} />
        <Stack spacing={2}>
          <Skeleton variant="rounded" height={180} />
          <Skeleton variant="rounded" height={200} />
        </Stack>
      </Box>

      <LinearProgress sx={{ mt: 2 }} />
    </>
  );
}
