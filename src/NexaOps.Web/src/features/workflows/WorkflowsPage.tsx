import { useCallback } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Box,
  Button,
  Card,
  LinearProgress,
  Stack,
  Switch,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TablePagination,
  TableRow,
  Tabs,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import { workflowsApi } from '@/api/workflows';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { useAuth } from '@/auth/useAuth';
import { PageHeader } from '@/components/PageHeader';
import { EmptyState } from '@/components/EmptyState';
import { ErrorState } from '@/components/ErrorState';
import { RunStatusChip, StepStatusChip, TriggerChip } from './WorkflowChips';
import { formatDateTime, formatRelative, humanise } from '@/utils/format';

/**
 * Automation rules, with the run history on a second tab.
 *
 * The history is not an afterthought: "why did my rule not fire" is the question people actually
 * have about automation, and it is unanswerable from a list of rules alone.
 */
export function WorkflowsPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { hasPermission } = useAuth();
  const [params, setParams] = useSearchParams();

  const tab = params.get('tab') === 'runs' ? 'runs' : 'rules';
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

  const rules = useQuery({
    queryKey: ['workflows', page, pageSize],
    queryFn: ({ signal }) => workflowsApi.search({ page, pageSize }, signal),
    placeholderData: keepPreviousData,
    enabled: tab === 'rules',
  });

  const runs = useQuery({
    queryKey: ['workflow-runs', page, pageSize],
    queryFn: ({ signal }) => workflowsApi.runs({ page, pageSize }, signal),
    placeholderData: keepPreviousData,
    enabled: tab === 'runs',
  });

  const setActive = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      workflowsApi.setActive(id, isActive),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['workflows'] });
    },
  });

  const canManage = hasPermission(Permissions.workflowManage);

  return (
    <Box>
      <PageHeader
        title="Automation"
        subtitle="Rules that act on records as they change, and the record of what they did."
        actions={
          <Can permission={Permissions.workflowManage}>
            <Button
              variant="contained"
              startIcon={<AddIcon />}
              onClick={() => navigate('/workflows/new')}
            >
              New rule
            </Button>
          </Can>
        }
      />

      <Tabs
        value={tab}
        onChange={(_, value: string) => update({ tab: value === 'rules' ? null : value })}
        sx={{ mb: 2 }}
      >
        <Tab label="Rules" value="rules" />
        <Tab label="History" value="runs" />
      </Tabs>

      {tab === 'rules' && (
        <>
          {rules.isLoading && <LinearProgress />}
          {rules.error && <ErrorState error={rules.error} onRetry={() => void rules.refetch()} />}

          {setActive.error && (
            <Alert severity="error" sx={{ mb: 2 }}>
              {setActive.error.message}
            </Alert>
          )}

          {rules.data?.items.length === 0 && (
            <EmptyState
              title="No rules yet"
              description={
                canManage
                  ? 'A rule watches one module for one kind of change, and acts when its conditions match.'
                  : 'Nobody has set up automation for this tenant.'
              }
            />
          )}

          {rules.data && rules.data.items.length > 0 && (
            <Card variant="outlined">
              <TableContainer>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Active</TableCell>
                      <TableCell>Rule</TableCell>
                      <TableCell>Applies to</TableCell>
                      <TableCell>When</TableCell>
                      <TableCell align="right">Conditions</TableCell>
                      <TableCell align="right">Actions</TableCell>
                      <TableCell>Last run</TableCell>
                      <TableCell align="right">Runs</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {rules.data.items.map((rule) => (
                      <TableRow
                        key={rule.id}
                        hover
                        sx={{ cursor: canManage ? 'pointer' : 'default' }}
                        onClick={() => canManage && navigate(`/workflows/${rule.id}`)}
                      >
                        <TableCell onClick={(event) => event.stopPropagation()}>
                          <Switch
                            size="small"
                            checked={rule.isActive}
                            disabled={!canManage || setActive.isPending}
                            onChange={(event) =>
                              setActive.mutate({ id: rule.id, isActive: event.target.checked })
                            }
                            inputProps={{ 'aria-label': `Activate ${rule.name}` }}
                          />
                        </TableCell>
                        <TableCell>
                          <Typography variant="body2" fontWeight={600}>
                            {rule.name}
                          </Typography>
                          {rule.description && (
                            <Typography variant="caption" color="text.secondary">
                              {rule.description}
                            </Typography>
                          )}
                        </TableCell>
                        <TableCell>
                          <Typography variant="body2">{humanise(rule.module)}</Typography>
                        </TableCell>
                        <TableCell>
                          <TriggerChip trigger={rule.trigger} />
                        </TableCell>
                        <TableCell align="right">{rule.conditionCount}</TableCell>
                        <TableCell align="right">{rule.actionCount}</TableCell>
                        <TableCell>
                          <Typography variant="body2" color="text.secondary">
                            {rule.lastRunAt ? formatRelative(rule.lastRunAt) : 'Never'}
                          </Typography>
                        </TableCell>
                        <TableCell align="right">{rule.runCount}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>

              <TablePagination
                component="div"
                count={rules.data.totalCount}
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

      {tab === 'runs' && (
        <>
          {runs.isLoading && <LinearProgress />}
          {runs.error && <ErrorState error={runs.error} onRetry={() => void runs.refetch()} />}

          {runs.data?.items.length === 0 && (
            <EmptyState
              title="Nothing has run yet"
              description="Every evaluation is recorded here, including the ones that did nothing."
            />
          )}

          <Stack gap={1}>
            {(runs.data?.items ?? []).map((run) => (
              <Accordion key={run.id} disableGutters variant="outlined">
                <AccordionSummary expandIcon={<ExpandMoreIcon />}>
                  <Stack
                    direction={{ xs: 'column', sm: 'row' }}
                    gap={1.5}
                    alignItems={{ xs: 'flex-start', sm: 'center' }}
                    sx={{ width: '100%', pr: 2 }}
                  >
                    <RunStatusChip status={run.status} />

                    <Typography variant="body2" fontWeight={600} sx={{ flex: 1 }}>
                      {run.workflowName}
                    </Typography>

                    <Typography variant="body2" fontFamily="monospace">
                      {run.recordNumber}
                    </Typography>

                    <Typography variant="caption" color="text.secondary">
                      {formatDateTime(run.startedAt)}
                    </Typography>
                  </Stack>
                </AccordionSummary>

                <AccordionDetails>
                  {run.outcome && (
                    <Alert severity="info" sx={{ mb: 1.5 }}>
                      {run.outcome}
                    </Alert>
                  )}

                  {run.steps.length === 0 && !run.outcome && (
                    <Typography variant="body2" color="text.secondary">
                      No actions were attempted.
                    </Typography>
                  )}

                  <Stack gap={1}>
                    {run.steps.map((step) => (
                      <Stack
                        key={step.sequence}
                        direction="row"
                        gap={1.5}
                        alignItems="flex-start"
                        flexWrap="wrap"
                      >
                        <StepStatusChip status={step.status} />

                        <Box sx={{ flex: 1, minWidth: 200 }}>
                          <Typography variant="body2">{humanise(step.actionType)}</Typography>

                          {step.detail && (
                            <Typography variant="caption" color="text.secondary" display="block">
                              {step.detail}
                            </Typography>
                          )}

                          {step.error && (
                            <Typography variant="caption" color="error" display="block">
                              {step.error}
                            </Typography>
                          )}
                        </Box>
                      </Stack>
                    ))}
                  </Stack>
                </AccordionDetails>
              </Accordion>
            ))}
          </Stack>

          {runs.data && runs.data.totalCount > 0 && (
            <TablePagination
              component="div"
              count={runs.data.totalCount}
              page={Math.max(0, page - 1)}
              rowsPerPage={pageSize}
              rowsPerPageOptions={[10, 25, 50, 100]}
              onPageChange={(_, next) => update({ page: String(next + 1) })}
              onRowsPerPageChange={(event) => update({ pageSize: event.target.value, page: null })}
            />
          )}
        </>
      )}
    </Box>
  );
}
