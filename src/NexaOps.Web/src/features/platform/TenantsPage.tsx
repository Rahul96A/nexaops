import { useState } from 'react';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  InputAdornment,
  LinearProgress,
  MenuItem,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TablePagination,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import SearchIcon from '@mui/icons-material/Search';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import BlockIcon from '@mui/icons-material/Block';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import ApartmentOutlinedIcon from '@mui/icons-material/ApartmentOutlined';
import { platformApi } from '@/api/platform';
import { ApiError } from '@/api/client';
import type { TenantOnboardingResult, TenantStatus, TenantSummary } from '@/api/types';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { EmptyState } from '@/components/EmptyState';
import { ErrorState } from '@/components/ErrorState';
import { PageHeader } from '@/components/PageHeader';
import { formatDate, formatRelative } from '@/utils/format';

const statusColours: Record<TenantStatus, 'success' | 'info' | 'warning' | 'default'> = {
  Active: 'success',
  Trial: 'info',
  Suspended: 'warning',
  Closed: 'default',
};

/**
 * The service provider's customer list, and the screen that onboards a new one.
 *
 * Only visible to the platform operator's own staff. A customer holding every permission inside
 * their tenant still cannot see this route, and the API refuses them independently.
 */
export function TenantsPage() {
  const queryClient = useQueryClient();

  const [search, setSearch] = useState('');
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(25);
  const [onboarding, setOnboarding] = useState(false);
  const [onboarded, setOnboarded] = useState<TenantOnboardingResult | null>(null);
  const [suspending, setSuspending] = useState<TenantSummary | null>(null);

  const tenants = useQuery({
    queryKey: ['platform-tenants', search, page, pageSize],
    queryFn: ({ signal }) =>
      platformApi.tenants({ search: search || undefined, page: page + 1, pageSize }, signal),
    placeholderData: keepPreviousData,
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['platform-tenants'] });
  };

  const setStatus = useMutation({
    mutationFn: ({ id, status, reason }: { id: string; status: TenantStatus; reason?: string }) =>
      platformApi.setStatus(id, status, reason ?? null),
    onSuccess: () => {
      invalidate();
      setSuspending(null);
    },
  });

  return (
    <Box>
      <PageHeader
        title="Customers"
        subtitle="Every tenant on this platform. Onboarding creates the tenant and its first administrator."
        actions={
          <Can permission={Permissions.platformTenantManage}>
            <Button variant="contained" startIcon={<AddIcon />} onClick={() => setOnboarding(true)}>
              Onboard customer
            </Button>
          </Can>
        }
      />

      <Stack direction={{ xs: 'column', md: 'row' }} gap={2} sx={{ mb: 2 }} alignItems="center">
        <TextField
          size="small"
          placeholder="Search by code, name or legal name"
          defaultValue={search}
          onBlur={(event) => {
            setSearch(event.target.value);
            setPage(0);
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              setSearch((event.target as HTMLInputElement).value);
              setPage(0);
            }
          }}
          sx={{ minWidth: 360 }}
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

      {setStatus.error instanceof ApiError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {setStatus.error.userMessage}
        </Alert>
      )}

      {(tenants.isLoading || tenants.isFetching) && <LinearProgress />}
      {tenants.error && <ErrorState error={tenants.error} onRetry={() => void tenants.refetch()} />}

      {tenants.data && tenants.data.items.length === 0 && (
        <Card variant="outlined">
          <EmptyState
            icon={<ApartmentOutlinedIcon />}
            title={search ? 'No customer matches that search' : 'No customers yet'}
            description={
              search
                ? 'Try a different code or name.'
                : 'Onboard the first one to create a tenant and the administrator who runs it.'
            }
          />
        </Card>
      )}

      {tenants.data && tenants.data.items.length > 0 && (
        <Card variant="outlined">
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Customer</TableCell>
                  <TableCell>Code</TableCell>
                  <TableCell align="right">People</TableCell>
                  <TableCell>Region</TableCell>
                  <TableCell>Onboarded</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell />
                </TableRow>
              </TableHead>
              <TableBody>
                {tenants.data.items.map((tenant) => (
                  <TableRow key={tenant.id} hover>
                    <TableCell>
                      <Typography variant="body2" fontWeight={600}>
                        {tenant.name}
                      </Typography>
                      {tenant.legalName && (
                        <Typography variant="caption" color="text.secondary">
                          {tenant.legalName}
                        </Typography>
                      )}
                    </TableCell>

                    <TableCell>
                      <Typography variant="body2" sx={{ fontFamily: 'monospace' }}>
                        {tenant.code}
                      </Typography>
                    </TableCell>

                    <TableCell align="right">
                      {/* Active out of total: a customer with people who can no longer sign in
                          looks different from one that never grew. */}
                      <Typography variant="body2">
                        {tenant.activeUserCount} / {tenant.userCount}
                      </Typography>
                    </TableCell>

                    <TableCell>
                      <Typography variant="body2" color="text.secondary">
                        {tenant.dataRegion}
                      </Typography>
                    </TableCell>

                    <TableCell>
                      <Tooltip title={formatDate(tenant.createdAt)}>
                        <Typography variant="body2" color="text.secondary">
                          {formatRelative(tenant.createdAt)}
                        </Typography>
                      </Tooltip>
                    </TableCell>

                    <TableCell>
                      <Chip
                        size="small"
                        label={tenant.status}
                        color={statusColours[tenant.status]}
                        variant={tenant.status === 'Closed' ? 'outlined' : 'filled'}
                      />
                    </TableCell>

                    <TableCell align="right">
                      <Can permission={Permissions.platformTenantManage}>
                        {tenant.status === 'Suspended' ? (
                          <Tooltip title="Reactivate">
                            <IconButton
                              size="small"
                              aria-label={`Reactivate ${tenant.name}`}
                              onClick={() =>
                                setStatus.mutate({ id: tenant.id, status: 'Active' })
                              }
                            >
                              <PlayArrowIcon fontSize="small" />
                            </IconButton>
                          </Tooltip>
                        ) : (
                          <Tooltip title="Suspend">
                            <span>
                              <IconButton
                                size="small"
                                aria-label={`Suspend ${tenant.name}`}
                                disabled={tenant.status === 'Closed'}
                                onClick={() => setSuspending(tenant)}
                              >
                                <BlockIcon fontSize="small" />
                              </IconButton>
                            </span>
                          </Tooltip>
                        )}
                      </Can>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>

          <TablePagination
            component="div"
            count={tenants.data.totalCount}
            page={page}
            onPageChange={(_, next) => setPage(next)}
            rowsPerPage={pageSize}
            onRowsPerPageChange={(event) => {
              setPageSize(Number(event.target.value));
              setPage(0);
            }}
            rowsPerPageOptions={[25, 50, 100]}
          />
        </Card>
      )}

      {onboarding && (
        <OnboardDialog
          onClose={() => setOnboarding(false)}
          onDone={(result) => {
            setOnboarding(false);
            setOnboarded(result);
            invalidate();
          }}
        />
      )}

      {onboarded && <OnboardedDialog result={onboarded} onClose={() => setOnboarded(null)} />}

      {suspending && (
        <SuspendDialog
          tenant={suspending}
          busy={setStatus.isPending}
          onClose={() => setSuspending(null)}
          onConfirm={(reason) =>
            setStatus.mutate({ id: suspending.id, status: 'Suspended', reason })
          }
        />
      )}
    </Box>
  );
}

/** Creates the tenant and the one person who can then run it. */
function OnboardDialog({
  onClose,
  onDone,
}: {
  onClose: () => void;
  onDone: (result: TenantOnboardingResult) => void;
}) {
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [legalName, setLegalName] = useState('');
  const [primaryDomain, setPrimaryDomain] = useState('');
  const [status, setStatus] = useState<TenantStatus>('Trial');
  const [email, setEmail] = useState('');
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [jobTitle, setJobTitle] = useState('');

  const onboard = useMutation({
    mutationFn: () =>
      platformApi.onboard({
        code: code.trim().toLowerCase(),
        name: name.trim(),
        legalName: legalName.trim() || null,
        primaryDomain: primaryDomain.trim().toLowerCase() || null,
        status,
        administratorEmail: email.trim().toLowerCase(),
        administratorFirstName: firstName.trim(),
        administratorLastName: lastName.trim(),
        administratorJobTitle: jobTitle.trim() || null,
      }),
    onSuccess: onDone,
  });

  const canSubmit =
    code.trim().length >= 3 &&
    name.trim().length > 0 &&
    email.trim().length > 0 &&
    firstName.trim().length > 0 &&
    lastName.trim().length > 0;

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Onboard a customer</DialogTitle>
      <DialogContent>
        <Stack gap={2} sx={{ mt: 1 }}>
          {onboard.error instanceof ApiError && (
            <Alert severity="error">{onboard.error.userMessage}</Alert>
          )}

          <TextField
            label="Tenant code"
            value={code}
            onChange={(event) => setCode(event.target.value)}
            required
            helperText="Lower-case letters, digits and hyphens. Appears in support conversations and cannot be changed later."
            slotProps={{ htmlInput: { maxLength: 32, style: { fontFamily: 'monospace' } } }}
          />

          <TextField
            label="Customer name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            required
          />

          <TextField
            label="Registered legal name"
            value={legalName}
            onChange={(event) => setLegalName(event.target.value)}
            helperText="Optional. What appears on the contract."
          />

          <TextField
            label="Primary email domain"
            value={primaryDomain}
            onChange={(event) => setPrimaryDomain(event.target.value)}
            helperText="Optional. Used to route single sign-on once it is configured."
          />

          <TextField
            select
            label="Status"
            value={status}
            onChange={(event) => setStatus(event.target.value as TenantStatus)}
          >
            <MenuItem value="Trial">Trial</MenuItem>
            <MenuItem value="Active">Active</MenuItem>
          </TextField>

          <Typography variant="subtitle2" sx={{ mt: 1 }}>
            First administrator
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mt: -1 }}>
            They receive a one-time password and full administration of their own tenant — not of
            this platform. Everyone else in the customer is created by them.
          </Typography>

          <Stack direction={{ xs: 'column', sm: 'row' }} gap={2}>
            <TextField
              label="First name"
              value={firstName}
              onChange={(event) => setFirstName(event.target.value)}
              required
              sx={{ flex: 1 }}
            />
            <TextField
              label="Last name"
              value={lastName}
              onChange={(event) => setLastName(event.target.value)}
              required
              sx={{ flex: 1 }}
            />
          </Stack>

          <TextField
            label="Email address"
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            required
          />

          <TextField
            label="Job title"
            value={jobTitle}
            onChange={(event) => setJobTitle(event.target.value)}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={onboard.isPending}>
          Cancel
        </Button>
        <Button
          variant="contained"
          onClick={() => onboard.mutate()}
          disabled={!canSubmit || onboard.isPending}
        >
          {onboard.isPending ? 'Onboarding…' : 'Onboard'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/** Shows the administrator's one-time password. It cannot be retrieved after this. */
function OnboardedDialog({
  result,
  onClose,
}: {
  result: TenantOnboardingResult;
  onClose: () => void;
}) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    if (!result.temporaryPassword) {
      return;
    }

    try {
      await navigator.clipboard.writeText(result.temporaryPassword);
      setCopied(true);
    } catch {
      // Clipboard access can be refused outright. The password is on screen either way.
      setCopied(false);
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{result.tenant.name} is ready</DialogTitle>
      <DialogContent>
        <Stack gap={2}>
          <Alert severity="warning">
            This password is shown once. It is not stored anywhere it can be read back, so if you
            close this dialog without noting it, the administrator will need a new one.
          </Alert>

          <TextField
            label="Administrator"
            value={result.administratorEmail}
            slotProps={{ input: { readOnly: true } }}
          />

          <Stack direction="row" gap={1} alignItems="center">
            <TextField
              label="One-time password"
              value={result.temporaryPassword ?? ''}
              slotProps={{ input: { readOnly: true } }}
              sx={{ flex: 1, '& input': { fontFamily: 'monospace' } }}
            />
            <Tooltip title={copied ? 'Copied' : 'Copy'}>
              <IconButton onClick={() => void copy()} aria-label="Copy password">
                <ContentCopyIcon />
              </IconButton>
            </Tooltip>
          </Stack>

          <Typography variant="body2" color="text.secondary">
            They sign in with the tenant code <strong>{result.tenant.code}</strong> and will be
            asked to change this password immediately, which is what stops your copy of it from
            working.
          </Typography>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button variant="contained" onClick={onClose}>
          Done
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/** Suspension needs a reason, because it goes into the customer's own audit trail. */
function SuspendDialog({
  tenant,
  busy,
  onClose,
  onConfirm,
}: {
  tenant: TenantSummary;
  busy: boolean;
  onClose: () => void;
  onConfirm: (reason: string) => void;
}) {
  const [reason, setReason] = useState('');

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Suspend {tenant.name}?</DialogTitle>
      <DialogContent>
        <Stack gap={2} sx={{ mt: 1 }}>
          <Alert severity="warning">
            All {tenant.activeUserCount} active users are signed out and cannot sign in again
            until the customer is reactivated. Their data is untouched.
          </Alert>

          <TextField
            label="Reason"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            required
            multiline
            minRows={2}
            helperText="Recorded in this customer's audit trail, so their administrator can see why."
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>
          Cancel
        </Button>
        <Button
          variant="contained"
          color="warning"
          onClick={() => onConfirm(reason.trim())}
          disabled={busy || reason.trim().length === 0}
        >
          {busy ? 'Suspending…' : 'Suspend'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
