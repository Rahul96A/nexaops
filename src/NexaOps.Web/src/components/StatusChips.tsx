import { Avatar, Box, Chip, LinearProgress, Tooltip, Typography } from '@mui/material';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import PauseCircleOutlineIcon from '@mui/icons-material/PauseCircleOutline';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import type { IncidentStatus, Priority, SlaInstance } from '@/api/types';
import { priorityColors, slaColors } from '@/theme/theme';
import { formatDateTime, formatDuration, humanise, initials } from '@/utils/format';

/**
 * The small, repeated pieces of the service desk vocabulary: priority, status, SLA state and
 * people. Centralised so that a P1 looks identical on the dashboard, in a queue, and on a
 * record page - an agent should recognise severity by colour without reading the label.
 */

export function PriorityChip({ priority, size = 'small' }: { priority: Priority; size?: 'small' | 'medium' }) {
  const colour = priorityColors[priority];

  return (
    <Chip
      label={colour.label}
      size={size}
      sx={{
        backgroundColor: colour.main,
        color: colour.contrast,
        fontWeight: 700,
      }}
    />
  );
}

const STATUS_STYLES: Record<IncidentStatus, { color: string; background: string }> = {
  New: { color: '#0A2CA8', background: '#DCE4FF' },
  Assigned: { color: '#0A2CA8', background: '#EEF2FF' },
  InProgress: { color: '#8A5A00', background: '#FFF4D6' },
  Pending: { color: '#4A5567', background: '#EEF0F4' },
  Resolved: { color: '#1D7A46', background: '#DCF5E7' },
  Closed: { color: '#4A5567', background: '#E3E8F0' },
  Cancelled: { color: '#697588', background: '#EEF0F4' },
};

export function StatusChip({ status, size = 'small' }: { status: IncidentStatus; size?: 'small' | 'medium' }) {
  const style = STATUS_STYLES[status];

  return (
    <Chip
      label={humanise(status)}
      size={size}
      variant="outlined"
      sx={{
        color: style.color,
        backgroundColor: style.background,
        borderColor: 'transparent',
        fontWeight: 600,
      }}
    />
  );
}

/**
 * One SLA clock, shown as a labelled progress meter.
 *
 * The bar is the honest signal: it fills as the commitment is consumed and turns red on
 * breach. The numbers beside it are business minutes computed by the server against the
 * tenant's calendar, not a wall-clock difference computed here.
 */
export function SlaMeter({ sla, dense = false }: { sla: SlaInstance; dense?: boolean }) {
  const isBreached = sla.state === 'Breached';
  const isPaused = sla.state === 'Paused';
  const isMet = sla.state === 'Met';

  const colour = slaColors[sla.state];
  const percent = Math.min(100, Math.max(0, sla.consumedPercent));

  const label = isMet
    ? 'Met'
    : isBreached
      ? formatDuration(sla.remainingMinutes)
      : isPaused
        ? 'Paused'
        : `${formatDuration(sla.remainingMinutes)} left`;

  return (
    <Box sx={{ minWidth: dense ? 140 : 200 }}>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 0.5 }}>
        <Typography variant="caption" sx={{ fontWeight: 600, color: 'text.secondary' }}>
          {sla.targetType}
        </Typography>

        <Tooltip title={`Due ${formatDateTime(sla.dueAt)} · ${sla.name}`}>
          <Typography
            variant="caption"
            sx={{ fontWeight: 700, color: colour, display: 'flex', alignItems: 'center', gap: 0.5 }}
          >
            {isBreached && <ErrorOutlineIcon sx={{ fontSize: 14 }} />}
            {isPaused && <PauseCircleOutlineIcon sx={{ fontSize: 14 }} />}
            {isMet && <CheckCircleOutlineIcon sx={{ fontSize: 14 }} />}
            {label}
          </Typography>
        </Tooltip>
      </Box>

      <LinearProgress
        variant="determinate"
        value={percent}
        aria-label={`${sla.targetType} SLA, ${percent} percent consumed`}
        sx={{
          height: 6,
          borderRadius: 3,
          backgroundColor: 'action.hover',
          '& .MuiLinearProgress-bar': { backgroundColor: colour, borderRadius: 3 },
        }}
      />
    </Box>
  );
}

/**
 * A compact SLA badge for list rows, where a full meter would be too heavy.
 * Shows the nearest outstanding deadline, or a breach marker.
 */
export function SlaBadge({
  breached,
  dueAt,
}: {
  breached: boolean;
  dueAt?: string | null;
}) {
  if (breached) {
    return (
      <Chip
        icon={<ErrorOutlineIcon />}
        label="Breached"
        size="small"
        sx={{ backgroundColor: '#FDE7E7', color: '#C62828', fontWeight: 700 }}
      />
    );
  }

  if (!dueAt) {
    return (
      <Typography variant="caption" color="text.disabled">
        —
      </Typography>
    );
  }

  const minutesRemaining = (new Date(dueAt).getTime() - Date.now()) / 60000;
  const urgent = minutesRemaining < 120;

  return (
    <Tooltip title={`Due ${formatDateTime(dueAt)}`}>
      <Typography
        variant="caption"
        sx={{ fontWeight: 600, color: urgent ? 'warning.main' : 'text.secondary' }}
      >
        {formatDuration(minutesRemaining)}
      </Typography>
    </Tooltip>
  );
}

/** A person, rendered as a deterministic coloured avatar plus their name. */
export function UserChip({
  name,
  color,
  size = 26,
  showName = true,
}: {
  name?: string | null;
  color?: string | null;
  size?: number;
  showName?: boolean;
}) {
  if (!name) {
    return (
      <Typography variant="body2" color="text.disabled">
        Unassigned
      </Typography>
    );
  }

  const avatar = (
    <Avatar
      sx={{
        width: size,
        height: size,
        fontSize: size * 0.42,
        fontWeight: 700,
        bgcolor: color ?? 'primary.main',
      }}
    >
      {initials(name)}
    </Avatar>
  );

  if (!showName) {
    return <Tooltip title={name}>{avatar}</Tooltip>;
  }

  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, minWidth: 0 }}>
      {avatar}
      <Typography variant="body2" noWrap>
        {name}
      </Typography>
    </Box>
  );
}
