import { useId } from 'react';
import { Box, Stack, Typography, useTheme } from '@mui/material';

export interface TrendPoint {
  date: string;
  created: number;
  resolved: number;
}

/**
 * Created against resolved, per day.
 *
 * Drawn as inline SVG rather than with a charting library. One chart does not justify four
 * hundred kilobytes of dependency, and a hand-drawn one can be made to say what this chart is
 * actually for: whether the desk is keeping up. The two series crossing is the finding.
 *
 * The data is also exposed as a table to assistive technology, because a line nobody can read is
 * not a report.
 */
export function TrendChart({
  points,
  height = 220,
  label,
}: {
  points: TrendPoint[];
  height?: number;
  label: string;
}) {
  const theme = useTheme();
  const titleId = useId();

  if (points.length === 0) {
    return null;
  }

  const width = 800;
  const padding = { top: 12, right: 12, bottom: 26, left: 34 };
  const plotWidth = width - padding.left - padding.right;
  const plotHeight = height - padding.top - padding.bottom;

  // A flat zero series still needs a sensible axis, so the ceiling never drops below one.
  const peak = Math.max(1, ...points.map((p) => Math.max(p.created, p.resolved)));

  const x = (index: number) =>
    padding.left + (points.length === 1 ? plotWidth / 2 : (index / (points.length - 1)) * plotWidth);

  const y = (value: number) => padding.top + plotHeight - (value / peak) * plotHeight;

  const line = (pick: (p: TrendPoint) => number) =>
    points.map((point, index) => `${index === 0 ? 'M' : 'L'} ${x(index)} ${y(pick(point))}`).join(' ');

  // Enough ticks to orient, not so many they overlap on a narrow screen.
  const tickEvery = Math.max(1, Math.ceil(points.length / 7));

  return (
    <Box>
      <Stack direction="row" gap={2} sx={{ mb: 1 }}>
        <Legend colour={theme.palette.primary.main} label="Raised" />
        <Legend colour={theme.palette.success.main} label="Resolved" />
      </Stack>

      <Box
        component="svg"
        viewBox={`0 0 ${width} ${height}`}
        role="img"
        aria-labelledby={titleId}
        sx={{ width: '100%', height: 'auto', display: 'block' }}
      >
        <title id={titleId}>{label}</title>

        {/* Horizontal guides at nothing, halfway and the peak. Three lines orient the eye; a
            full grid competes with the data. */}
        {[0, peak / 2, peak].map((value) => (
          <g key={value}>
            <line
              x1={padding.left}
              x2={width - padding.right}
              y1={y(value)}
              y2={y(value)}
              stroke={theme.palette.divider}
              strokeWidth={1}
            />
            <text
              x={padding.left - 6}
              y={y(value) + 4}
              textAnchor="end"
              fontSize={11}
              fill={theme.palette.text.secondary}
            >
              {Math.round(value)}
            </text>
          </g>
        ))}

        <path d={line((p) => p.created)} fill="none" stroke={theme.palette.primary.main} strokeWidth={2} />
        <path d={line((p) => p.resolved)} fill="none" stroke={theme.palette.success.main} strokeWidth={2} />

        {points.map((point, index) =>
          index % tickEvery === 0 ? (
            <text
              key={point.date}
              x={x(index)}
              y={height - 8}
              textAnchor="middle"
              fontSize={11}
              fill={theme.palette.text.secondary}
            >
              {point.date.slice(5)}
            </text>
          ) : null,
        )}
      </Box>

      {/* The same numbers, for anyone reading with a screen reader or checking the chart. */}
      <Box component="table" sx={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' }}>
        <caption>{label}</caption>
        <thead>
          <tr>
            <th scope="col">Date</th>
            <th scope="col">Raised</th>
            <th scope="col">Resolved</th>
          </tr>
        </thead>
        <tbody>
          {points.map((point) => (
            <tr key={point.date}>
              <th scope="row">{point.date}</th>
              <td>{point.created}</td>
              <td>{point.resolved}</td>
            </tr>
          ))}
        </tbody>
      </Box>
    </Box>
  );
}

function Legend({ colour, label }: { colour: string; label: string }) {
  return (
    <Stack direction="row" gap={0.75} alignItems="center">
      <Box sx={{ width: 12, height: 3, borderRadius: 1, bgcolor: colour }} />
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
    </Stack>
  );
}
