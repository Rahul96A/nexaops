import { useNavigate, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Card,
  CardContent,
  Chip,
  Divider,
  LinearProgress,
  List,
  ListItem,
  ListItemText,
  Stack,
  Typography,
} from '@mui/material';
import ReportProblemOutlinedIcon from '@mui/icons-material/ReportProblemOutlined';
import { cmdbApi } from '@/api/cmdb';
import type { RelatedItem } from '@/api/types';
import { PageHeader } from '@/components/PageHeader';
import { ErrorState } from '@/components/ErrorState';
import { CiStatusChip, CriticalityChip } from './CmdbChips';
import { formatDate, humanise } from '@/utils/format';

/**
 * One configuration item, with the two answers the module exists to give.
 *
 * Impact and dependency are shown as separate lists rather than one merged graph, because they
 * answer different questions: who do I warn, and what do I check first.
 */
export function CiDetailPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();

  const { data: item, isLoading, error, refetch } = useQuery({
    queryKey: ['ci', id],
    queryFn: ({ signal }) => cmdbApi.get(id, signal),
    enabled: Boolean(id),
  });

  if (isLoading) {
    return <LinearProgress />;
  }

  if (error) {
    return <ErrorState error={error} onRetry={() => void refetch()} />;
  }

  if (!item) {
    return null;
  }

  return (
    <Box>
      <PageHeader
        title={item.name}
        subtitle={item.description ?? humanise(item.type)}
        crumbs={[{ label: 'Configuration items', to: '/cmdb' }, { label: item.number }]}
      />

      {item.isOutOfSupport && (
        <Alert icon={<ReportProblemOutlinedIcon />} severity="error" sx={{ mb: 2 }}>
          <strong>Support cover has lapsed.</strong> It ended on{' '}
          {item.supportExpiresOn ? formatDate(item.supportExpiresOn) : 'an earlier date'}. Anything
          that goes wrong here has no vendor route.
        </Alert>
      )}

      {!item.ownerUserId && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          <strong>Nobody owns this.</strong> An unowned item is one nobody maintains, and one
          nobody can be asked about during an outage.
        </Alert>
      )}

      <Stack direction={{ xs: 'column', lg: 'row' }} gap={3} alignItems="flex-start">
        <Stack gap={3} sx={{ flex: 1, width: '100%' }}>
          <RelatedCard
            title="What stops working if this fails"
            emptyMessage="Nothing depends on this item."
            items={item.impacts}
            onOpen={(target) => navigate(`/cmdb/${target}`)}
          />

          <RelatedCard
            title="What this relies on"
            emptyMessage="This item does not depend on anything recorded here."
            items={item.dependsOn}
            onOpen={(target) => navigate(`/cmdb/${target}`)}
          />
        </Stack>

        <Card variant="outlined" sx={{ width: { xs: '100%', lg: 320 }, flexShrink: 0 }}>
          <CardContent>
            <Stack gap={1.5}>
              <Field label="Status">
                <CiStatusChip status={item.status} />
              </Field>
              <Field label="Criticality">
                <CriticalityChip criticality={item.criticality} />
              </Field>
              <Field label="Type">{humanise(item.type)}</Field>
              <Field label="Environment">{item.environment ?? '—'}</Field>
              <Field label="Location">{item.location ?? '—'}</Field>

              <Divider />

              <Field label="Owner">{item.ownerName ?? 'Unowned'}</Field>
              <Field label="Support group">{item.supportGroupName ?? '—'}</Field>
              <Field label="Open incidents">{String(item.openIncidentCount)}</Field>

              <Divider />

              {item.manufacturer && <Field label="Manufacturer">{item.manufacturer}</Field>}
              {item.model && <Field label="Model">{item.model}</Field>}
              {item.version && <Field label="Version">{item.version}</Field>}
              {item.serialNumber && <Field label="Serial">{item.serialNumber}</Field>}
              {item.vendor && <Field label="Vendor">{item.vendor}</Field>}

              <Field label="Support until">
                {item.supportExpiresOn ? formatDate(item.supportExpiresOn) : '—'}
              </Field>
            </Stack>
          </CardContent>
        </Card>
      </Stack>
    </Box>
  );
}

function RelatedCard({
  title,
  emptyMessage,
  items,
  onOpen,
}: {
  title: string;
  emptyMessage: string;
  items: RelatedItem[];
  onOpen: (id: string) => void;
}) {
  return (
    <Card variant="outlined">
      <CardContent>
        <Stack direction="row" gap={1} alignItems="center" sx={{ mb: 1 }}>
          <Typography variant="subtitle2">{title}</Typography>
          <Chip label={items.length} size="small" />
        </Stack>

        {items.length === 0 && (
          <Typography variant="body2" color="text.secondary">
            {emptyMessage}
          </Typography>
        )}

        <List dense disablePadding>
          {items.map((related) => (
            <ListItem
              key={`${related.id}-${related.relationship}`}
              disableGutters
              sx={{ cursor: 'pointer' }}
              onClick={() => onOpen(related.id)}
            >
              <ListItemText
                primary={
                  <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap">
                    <Typography variant="body2" fontWeight={600}>
                      {related.name}
                    </Typography>
                    <CriticalityChip criticality={related.criticality} />
                    <CiStatusChip status={related.status} />

                    {/*
                      Depth is shown because it changes who you call first. A directly affected
                      item is somebody's immediate problem; four hops away is a heads-up.
                    */}
                    <Chip
                      label={related.depth === 1 ? 'Direct' : `${related.depth} hops`}
                      size="small"
                      variant="outlined"
                      color={related.depth === 1 ? 'primary' : 'default'}
                    />
                  </Stack>
                }
                secondary={
                  <Typography variant="caption" color="text.secondary">
                    {humanise(related.type)} · via {humanise(related.relationship).toLowerCase()}
                  </Typography>
                }
              />
            </ListItem>
          ))}
        </List>
      </CardContent>
    </Card>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Box sx={{ textAlign: 'right' }}>
        {typeof children === 'string' ? (
          <Typography variant="body2">{children}</Typography>
        ) : (
          children
        )}
      </Box>
    </Stack>
  );
}
