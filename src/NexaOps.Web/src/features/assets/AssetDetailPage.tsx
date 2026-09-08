import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Card,
  CardContent,
  CardHeader,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  LinearProgress,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { assetsApi } from '@/api/assets';
import { referenceApi } from '@/api/reference';
import { ApiError } from '@/api/client';
import type { AssetStatus, UserSummary } from '@/api/types';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { PageHeader } from '@/components/PageHeader';
import { ErrorState } from '@/components/ErrorState';
import { AssetStatusChip } from '@/features/cmdb/CmdbChips';
import { formatCurrency, formatDate, formatDateTime, humanise } from '@/utils/format';

/** Where a handback can legitimately land. Mirrors the server, which enforces it. */
const returnDestinations: { value: AssetStatus; label: string; help: string }[] = [
  { value: 'InStock', label: 'Back into stock', help: 'Ready to be issued to somebody else.' },
  { value: 'InRepair', label: 'Into repair', help: 'Came back faulty.' },
  {
    value: 'Lost',
    label: 'Unaccounted for',
    help: 'Never handed back. Recorded as a security question, not a bookkeeping one.',
  },
];

/**
 * One asset: what it is, who has it, and everybody who has ever had it.
 *
 * The custody history is the point of the page. An asset register that only stores the current
 * holder cannot answer "who had this in March", which is the question that actually gets asked.
 */
export function AssetDetailPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [issueOpen, setIssueOpen] = useState(false);
  const [returnOpen, setReturnOpen] = useState(false);

  const { data: asset, isLoading, error, refetch } = useQuery({
    queryKey: ['asset', id],
    queryFn: ({ signal }) => assetsApi.get(id, signal),
    enabled: Boolean(id),
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['asset', id] });
    void queryClient.invalidateQueries({ queryKey: ['assets'] });
    void queryClient.invalidateQueries({ queryKey: ['asset-summary'] });
  };

  const issue = useMutation({
    mutationFn: (input: { userId: string; note?: string }) => assetsApi.assign(id, input),
    onSuccess: () => {
      setIssueOpen(false);
      invalidate();
    },
  });

  const takeBack = useMutation({
    mutationFn: (input: { note?: string; returnTo: AssetStatus }) => assetsApi.return(id, input),
    onSuccess: () => {
      setReturnOpen(false);
      invalidate();
    },
  });

  if (isLoading) {
    return <LinearProgress />;
  }

  if (error) {
    return <ErrorState error={error} onRetry={() => void refetch()} />;
  }

  if (!asset) {
    return null;
  }

  const actionError = issue.error ?? takeBack.error;

  return (
    <Box>
      <PageHeader
        title={asset.name}
        subtitle={`${asset.assetTag} · ${humanise(asset.kind)}`}
        crumbs={[{ label: 'Assets', to: '/assets' }, { label: asset.number }]}
        actions={
          <Can permission={Permissions.assetAssign}>
            <Stack direction="row" gap={1}>
              {asset.assignedToUserId ? (
                <Button variant="contained" onClick={() => setReturnOpen(true)}>
                  Take back
                </Button>
              ) : (
                <Button
                  variant="contained"
                  disabled={asset.status === 'Disposed' || asset.status === 'Retired'}
                  onClick={() => setIssueOpen(true)}
                >
                  Issue to someone
                </Button>
              )}
            </Stack>
          </Can>
        }
      />

      {actionError instanceof ApiError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {actionError.message}
        </Alert>
      )}

      {asset.status === 'Lost' && (
        <Alert severity="error" sx={{ mb: 2 }}>
          <strong>This device is unaccounted for.</strong> If it held company data, that is a
          security matter as well as a register one.
        </Alert>
      )}

      {asset.status === 'Disposed' && (
        <Alert severity="info" sx={{ mb: 2 }}>
          Disposed of {asset.disposedOn ? `on ${formatDate(asset.disposedOn)}` : ''}.
          {asset.disposalNotes ? ` ${asset.disposalNotes}` : ''}
        </Alert>
      )}

      {asset.isOutOfWarranty && asset.status !== 'Disposed' && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          Out of warranty since{' '}
          {asset.warrantyExpiresOn ? formatDate(asset.warrantyExpiresOn) : 'an earlier date'}. A
          repair on this is billable.
        </Alert>
      )}

      <Stack direction={{ xs: 'column', lg: 'row' }} gap={3} alignItems="flex-start">
        <Stack gap={3} sx={{ flex: 1, width: '100%' }}>
          <Card variant="outlined">
            <CardHeader title="Custody" titleTypographyProps={{ variant: 'h4' }} />
            <CardContent sx={{ pt: 0 }}>
              {asset.custodyHistory.length === 0 && (
                <Typography variant="body2" color="text.secondary">
                  This asset has never been issued to anybody.
                </Typography>
              )}

              <Stack gap={1.5}>
                {asset.custodyHistory.map((entry) => (
                  <Box
                    key={entry.id}
                    sx={{
                      borderLeft: 3,
                      borderColor: entry.returnedAt ? 'divider' : 'primary.main',
                      pl: 2,
                      py: 0.5,
                    }}
                  >
                    <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap">
                      <Typography variant="subtitle2">{entry.userName}</Typography>
                      {!entry.returnedAt && <Chip label="Holds it now" size="small" color="primary" />}
                    </Stack>

                    <Typography variant="caption" color="text.secondary" display="block">
                      {formatDateTime(entry.assignedAt)} —{' '}
                      {entry.returnedAt ? formatDateTime(entry.returnedAt) : 'present'}
                    </Typography>

                    {entry.assignmentNote && (
                      <Typography variant="body2" sx={{ mt: 0.5 }}>
                        Issued: {entry.assignmentNote}
                      </Typography>
                    )}

                    {entry.returnNote && (
                      <Typography variant="body2">Returned: {entry.returnNote}</Typography>
                    )}
                  </Box>
                ))}
              </Stack>
            </CardContent>
          </Card>

          <Card variant="outlined">
            <CardHeader title="Purchase" titleTypographyProps={{ variant: 'h4' }} />
            <CardContent sx={{ pt: 0 }}>
              <Stack gap={1.5}>
                <Field label="Purchased">
                  {asset.purchasedOn ? formatDate(asset.purchasedOn) : '—'}
                </Field>
                <Field label="Cost">
                  {asset.purchaseCost != null ? formatCurrency(asset.purchaseCost) : '—'}
                </Field>
                <Field label="Vendor">{asset.vendor ?? '—'}</Field>
                <Field label="Purchase order">{asset.purchaseOrderNumber ?? '—'}</Field>
                <Field label="Warranty until">
                  {asset.warrantyExpiresOn ? formatDate(asset.warrantyExpiresOn) : '—'}
                </Field>
                <Field label="Useful life">
                  {asset.usefulLifeMonths ? `${asset.usefulLifeMonths} months` : '—'}
                </Field>
                <Field label="Refresh due">
                  {/*
                    Null when there is no purchase date or no stated life. Showing a guessed date
                    would put a replacement budget on a number nobody entered.
                  */}
                  {asset.refreshDueOn ? formatDate(asset.refreshDueOn) : 'Not calculable'}
                </Field>
              </Stack>
            </CardContent>
          </Card>
        </Stack>

        <Card variant="outlined" sx={{ width: { xs: '100%', lg: 320 }, flexShrink: 0 }}>
          <CardContent>
            <Stack gap={1.5}>
              <Field label="Status">
                <AssetStatusChip status={asset.status} />
              </Field>
              <Field label="Held by">{asset.assignedToName ?? 'Nobody'}</Field>
              <Field label="Since">
                {asset.assignedAt ? formatDateTime(asset.assignedAt) : '—'}
              </Field>
              <Field label="Location">{asset.location ?? '—'}</Field>

              <Divider />

              <Field label="Tag">{asset.assetTag}</Field>
              <Field label="Manufacturer">{asset.manufacturer ?? '—'}</Field>
              <Field label="Model">{asset.model ?? '—'}</Field>
              <Field label="Serial">{asset.serialNumber ?? '—'}</Field>

              {asset.configurationItemId && (
                <>
                  <Divider />
                  <Button
                    size="small"
                    onClick={() => navigate(`/cmdb/${asset.configurationItemId}`)}
                  >
                    {asset.configurationItemName ?? 'Linked configuration item'}
                  </Button>
                </>
              )}
            </Stack>
          </CardContent>
        </Card>
      </Stack>

      <IssueDialog
        open={issueOpen}
        pending={issue.isPending}
        onClose={() => setIssueOpen(false)}
        onSubmit={(userId, note) => issue.mutate({ userId, note: note || undefined })}
      />

      <ReturnDialog
        open={returnOpen}
        holder={asset.assignedToName ?? 'the current holder'}
        pending={takeBack.isPending}
        onClose={() => setReturnOpen(false)}
        onSubmit={(returnTo, note) => takeBack.mutate({ returnTo, note: note || undefined })}
      />
    </Box>
  );
}

function IssueDialog({
  open,
  pending,
  onClose,
  onSubmit,
}: {
  open: boolean;
  pending: boolean;
  onClose: () => void;
  onSubmit: (userId: string, note: string) => void;
}) {
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<UserSummary | null>(null);
  const [note, setNote] = useState('');

  const { data: users, isFetching } = useQuery({
    queryKey: ['user-search', search],
    queryFn: ({ signal }) => referenceApi.searchUsers(search, signal),
    // The directory can be large, so this only queries once there is enough to narrow it.
    enabled: open && search.trim().length >= 2,
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Issue this asset</DialogTitle>
      <DialogContent>
        <Stack gap={2} sx={{ pt: 1 }}>
          <Autocomplete
            options={users ?? []}
            value={selected}
            onChange={(_, value) => setSelected(value)}
            onInputChange={(_, value) => setSearch(value)}
            getOptionLabel={(option) => option.displayName}
            isOptionEqualToValue={(option, value) => option.id === value.id}
            loading={isFetching}
            noOptionsText={
              search.trim().length < 2 ? 'Type at least two characters' : 'Nobody matches'
            }
            renderInput={(params) => (
              <TextField
                {...params}
                label="Issue to"
                slotProps={{
                  input: {
                    ...params.InputProps,
                    endAdornment: (
                      <>
                        {isFetching && <CircularProgress size={16} />}
                        {params.InputProps.endAdornment}
                      </>
                    ),
                  },
                }}
              />
            )}
          />

          <TextField
            label="Note"
            placeholder="Why they have it, or what was handed over with it."
            value={note}
            onChange={(event) => setNote(event.target.value)}
            multiline
            minRows={2}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={!selected || pending}
          onClick={() => selected && onSubmit(selected.id, note)}
        >
          Issue
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function ReturnDialog({
  open,
  holder,
  pending,
  onClose,
  onSubmit,
}: {
  open: boolean;
  holder: string;
  pending: boolean;
  onClose: () => void;
  onSubmit: (returnTo: AssetStatus, note: string) => void;
}) {
  const [returnTo, setReturnTo] = useState<AssetStatus>('InStock');
  const [note, setNote] = useState('');

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Take this asset back from {holder}</DialogTitle>
      <DialogContent>
        <Stack gap={2} sx={{ pt: 1 }}>
          <TextField
            select
            label="Where does it go"
            value={returnTo}
            onChange={(event) => setReturnTo(event.target.value as AssetStatus)}
            helperText={returnDestinations.find((option) => option.value === returnTo)?.help}
          >
            {returnDestinations.map((option) => (
              <MenuItem key={option.value} value={option.value}>
                {option.label}
              </MenuItem>
            ))}
          </TextField>

          <TextField
            label="Note"
            placeholder="Condition it came back in, or what is missing."
            value={note}
            onChange={(event) => setNote(event.target.value)}
            multiline
            minRows={2}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={pending} onClick={() => onSubmit(returnTo, note)}>
          Take back
        </Button>
      </DialogActions>
    </Dialog>
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
