import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CardHeader,
  Divider,
  FormControlLabel,
  IconButton,
  LinearProgress,
  MenuItem,
  Stack,
  Switch,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import { workflowsApi } from '@/api/workflows';
import { referenceApi } from '@/api/reference';
import { ApiError } from '@/api/client';
import type {
  Priority,
  UpsertWorkflowActionInput,
  UpsertWorkflowConditionInput,
  WorkflowActionType,
  WorkflowConditionOperator,
  WorkflowModule,
  WorkflowRecipient,
  WorkflowTrigger,
} from '@/api/types';
import { PageHeader } from '@/components/PageHeader';
import { ErrorState } from '@/components/ErrorState';

const modules: { value: WorkflowModule; label: string }[] = [
  { value: 'Incident', label: 'Incidents' },
  { value: 'Request', label: 'Service requests' },
  { value: 'Problem', label: 'Problems' },
  { value: 'Change', label: 'Changes' },
];

const triggers: { value: WorkflowTrigger; label: string }[] = [
  { value: 'RecordCreated', label: 'is raised' },
  { value: 'StatusChanged', label: 'changes status' },
  { value: 'PriorityChanged', label: 'changes priority' },
  { value: 'AssignmentChanged', label: 'is reassigned' },
];

const operators: { value: WorkflowConditionOperator; label: string; needsValue: boolean }[] = [
  { value: 'Equals', label: 'is', needsValue: true },
  { value: 'NotEquals', label: 'is not', needsValue: true },
  { value: 'In', label: 'is one of', needsValue: true },
  { value: 'Contains', label: 'contains', needsValue: true },
  { value: 'GreaterThan', label: 'is greater than', needsValue: true },
  { value: 'LessThan', label: 'is less than', needsValue: true },
  { value: 'IsEmpty', label: 'is empty', needsValue: false },
  { value: 'IsNotEmpty', label: 'has a value', needsValue: false },
];

const actionTypes: { value: WorkflowActionType; label: string }[] = [
  { value: 'NotifyUser', label: 'Notify somebody' },
  { value: 'NotifyGroup', label: 'Notify a group' },
  { value: 'AssignToGroup', label: 'Route to a group' },
  { value: 'AssignToUser', label: 'Assign to a person' },
  { value: 'SetPriority', label: 'Raise priority' },
  { value: 'RequestApproval', label: 'Request approval' },
];

const recipients: { value: WorkflowRecipient; label: string }[] = [
  { value: 'Requester', label: 'The requester' },
  { value: 'Assignee', label: 'The assignee' },
  { value: 'AssignmentGroup', label: 'The assignment group' },
  { value: 'SpecificUser', label: 'A named person' },
  { value: 'SpecificGroup', label: 'A named group' },
];

const priorities: { value: Priority; label: string }[] = [
  { value: 'P1Critical', label: 'P1 — Critical' },
  { value: 'P2High', label: 'P2 — High' },
  { value: 'P3Moderate', label: 'P3 — Moderate' },
  { value: 'P4Low', label: 'P4 — Low' },
];

const emptyCondition: UpsertWorkflowConditionInput = {
  field: '',
  operator: 'Equals',
  value: '',
};

const emptyAction: UpsertWorkflowActionInput = {
  sequence: 0,
  type: 'NotifyUser',
  recipient: 'Requester',
};

/**
 * Writing one automation rule.
 *
 * The form is the rule's stored shape: a trigger, a list of conditions combined with AND, and an
 * ordered list of actions. There is no canvas and no branching — a rule you can read top to
 * bottom is one you can still explain after it has done something surprising.
 */
export function WorkflowEditorPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const isNew = !id;

  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [module, setModule] = useState<WorkflowModule>('Incident');
  const [trigger, setTrigger] = useState<WorkflowTrigger>('RecordCreated');
  const [isActive, setIsActive] = useState(true);
  const [sequence, setSequence] = useState(0);
  const [conditions, setConditions] = useState<UpsertWorkflowConditionInput[]>([]);
  const [actions, setActions] = useState<UpsertWorkflowActionInput[]>([{ ...emptyAction }]);
  const [rowVersion, setRowVersion] = useState<string | null>(null);

  const existing = useQuery({
    queryKey: ['workflow', id],
    queryFn: ({ signal }) => workflowsApi.get(id!, signal),
    enabled: !isNew,
  });

  const fields = useQuery({
    queryKey: ['workflow-fields', module],
    queryFn: ({ signal }) => workflowsApi.fields(module, signal),
  });

  const groups = useQuery({
    queryKey: ['groups', 'Assignment'],
    queryFn: ({ signal }) => referenceApi.groups('Assignment', signal),
  });

  useEffect(() => {
    if (!existing.data) {
      return;
    }

    const rule = existing.data;
    setName(rule.name);
    setDescription(rule.description ?? '');
    setModule(rule.module);
    setTrigger(rule.trigger);
    setIsActive(rule.isActive);
    setSequence(rule.sequence);
    setConditions(
      rule.conditions.map((c) => ({ field: c.field, operator: c.operator, value: c.value ?? '' })),
    );
    setActions(
      rule.actions.map((a) => ({
        sequence: a.sequence,
        type: a.type,
        recipient: a.recipient,
        targetGroupId: a.targetGroupId,
        targetUserId: a.targetUserId,
        targetPriority: a.targetPriority,
        message: a.message,
      })),
    );
    setRowVersion(rule.rowVersion ?? null);
  }, [existing.data]);

  const save = useMutation({
    mutationFn: () => {
      const input = {
        name,
        description: description || null,
        module,
        trigger,
        isActive,
        sequence,
        conditions: conditions.map((c) => ({ ...c, value: c.value || null })),
        actions: actions.map((a, index) => ({ ...a, sequence: index })),
        rowVersion,
      };

      return isNew ? workflowsApi.create(input) : workflowsApi.update(id!, input);
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ['workflows'] });
      void queryClient.invalidateQueries({ queryKey: ['workflow', saved.id] });
      navigate('/workflows');
    },
  });

  if (!isNew && existing.isLoading) {
    return <LinearProgress />;
  }

  if (!isNew && existing.error) {
    return <ErrorState error={existing.error} onRetry={() => void existing.refetch()} />;
  }

  const fieldOptions = fields.data ?? [];

  return (
    <Box>
      <PageHeader
        title={isNew ? 'New automation rule' : name}
        subtitle="When something happens, and these things are true, do these things."
        crumbs={[{ label: 'Automation', to: '/workflows' }, { label: isNew ? 'New rule' : name }]}
        actions={
          <Stack direction="row" gap={1}>
            <Button onClick={() => navigate('/workflows')}>Cancel</Button>
            <Button
              variant="contained"
              disabled={!name.trim() || save.isPending}
              onClick={() => save.mutate()}
            >
              Save
            </Button>
          </Stack>
        }
      />

      {save.error instanceof ApiError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {save.error.message}
          {Object.entries(save.error.fieldErrors).map(([field, messages]) => (
            <Typography key={field} variant="body2">
              {messages.join(' ')}
            </Typography>
          ))}
        </Alert>
      )}

      <Stack gap={3}>
        <Card variant="outlined">
          <CardHeader title="When" titleTypographyProps={{ variant: 'h4' }} />
          <CardContent sx={{ pt: 0 }}>
            <Stack gap={2}>
              <TextField
                label="Rule name"
                value={name}
                onChange={(event) => setName(event.target.value)}
                helperText="This is what appears in the run history, so make it say what the rule does."
                required
              />

              <TextField
                label="Description"
                value={description}
                onChange={(event) => setDescription(event.target.value)}
                multiline
                minRows={2}
              />

              <Stack direction={{ xs: 'column', sm: 'row' }} gap={2}>
                <TextField
                  select
                  label="Applies to"
                  value={module}
                  onChange={(event) => {
                    setModule(event.target.value as WorkflowModule);

                    // The available fields change with the module, so conditions written against
                    // the old one would be refused on save. Clearing them is the honest response.
                    setConditions([]);
                  }}
                  disabled={!isNew}
                  sx={{ flex: 1 }}
                  helperText={isNew ? undefined : 'Fixed once the rule exists.'}
                >
                  {modules.map((option) => (
                    <MenuItem key={option.value} value={option.value}>
                      {option.label}
                    </MenuItem>
                  ))}
                </TextField>

                <TextField
                  select
                  label="When the record"
                  value={trigger}
                  onChange={(event) => setTrigger(event.target.value as WorkflowTrigger)}
                  disabled={!isNew}
                  sx={{ flex: 1 }}
                  helperText={isNew ? undefined : 'Fixed once the rule exists.'}
                >
                  {triggers.map((option) => (
                    <MenuItem key={option.value} value={option.value}>
                      {option.label}
                    </MenuItem>
                  ))}
                </TextField>

                <TextField
                  type="number"
                  label="Order"
                  value={sequence}
                  onChange={(event) => setSequence(Number(event.target.value))}
                  sx={{ width: 120 }}
                  helperText="Lower runs first"
                />
              </Stack>

              <FormControlLabel
                control={
                  <Switch checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />
                }
                label="Active"
              />
            </Stack>
          </CardContent>
        </Card>

        <Card variant="outlined">
          <CardHeader
            title="And all of these are true"
            titleTypographyProps={{ variant: 'h4' }}
            subheader="Conditions combine with AND. For an either/or, write two rules."
            action={
              <Button
                size="small"
                startIcon={<AddIcon />}
                onClick={() => setConditions((current) => [...current, { ...emptyCondition }])}
              >
                Add condition
              </Button>
            }
          />
          <CardContent sx={{ pt: 0 }}>
            {conditions.length === 0 && (
              <Typography variant="body2" color="text.secondary">
                No conditions. The rule will run on every {module.toLowerCase()} that hits the
                trigger.
              </Typography>
            )}

            <Stack gap={2}>
              {conditions.map((condition, index) => {
                const operator = operators.find((o) => o.value === condition.operator);
                const field = fieldOptions.find((f) => f.field === condition.field);

                return (
                  <Stack key={index} direction="row" gap={1.5} alignItems="flex-start">
                    <TextField
                      select
                      label="Field"
                      value={condition.field}
                      onChange={(event) =>
                        setConditions((current) =>
                          current.map((c, i) =>
                            i === index ? { ...c, field: event.target.value } : c,
                          ),
                        )
                      }
                      sx={{ flex: 1 }}
                      size="small"
                    >
                      {fieldOptions.map((option) => (
                        <MenuItem key={option.field} value={option.field}>
                          {option.label}
                        </MenuItem>
                      ))}
                    </TextField>

                    <TextField
                      select
                      label="Test"
                      value={condition.operator}
                      onChange={(event) =>
                        setConditions((current) =>
                          current.map((c, i) =>
                            i === index
                              ? { ...c, operator: event.target.value as WorkflowConditionOperator }
                              : c,
                          ),
                        )
                      }
                      sx={{ width: 170 }}
                      size="small"
                    >
                      {operators.map((option) => (
                        <MenuItem key={option.value} value={option.value}>
                          {option.label}
                        </MenuItem>
                      ))}
                    </TextField>

                    <TextField
                      label="Value"
                      value={condition.value ?? ''}
                      onChange={(event) =>
                        setConditions((current) =>
                          current.map((c, i) =>
                            i === index ? { ...c, value: event.target.value } : c,
                          ),
                        )
                      }
                      disabled={!operator?.needsValue}
                      helperText={field?.hint ?? undefined}
                      sx={{ flex: 1 }}
                      size="small"
                    />

                    {field?.hint && (
                      <Tooltip title={field.hint}>
                        <InfoOutlinedIcon fontSize="small" color="action" sx={{ mt: 1.25 }} />
                      </Tooltip>
                    )}

                    <IconButton
                      aria-label="Remove condition"
                      onClick={() =>
                        setConditions((current) => current.filter((_, i) => i !== index))
                      }
                    >
                      <DeleteOutlineIcon fontSize="small" />
                    </IconButton>
                  </Stack>
                );
              })}
            </Stack>
          </CardContent>
        </Card>

        <Card variant="outlined">
          <CardHeader
            title="Then do this"
            titleTypographyProps={{ variant: 'h4' }}
            subheader="In order. An action that fails does not stop the rest."
            action={
              <Button
                size="small"
                startIcon={<AddIcon />}
                onClick={() =>
                  setActions((current) => [...current, { ...emptyAction, sequence: current.length }])
                }
              >
                Add action
              </Button>
            }
          />
          <CardContent sx={{ pt: 0 }}>
            <Stack gap={2}>
              {actions.map((action, index) => (
                <Box key={index}>
                  {index > 0 && <Divider sx={{ mb: 2 }} />}

                  <Stack direction="row" gap={1.5} alignItems="flex-start" flexWrap="wrap">
                    <TextField
                      select
                      label="Action"
                      value={action.type}
                      onChange={(event) =>
                        setActions((current) =>
                          current.map((a, i) =>
                            i === index
                              ? { ...a, type: event.target.value as WorkflowActionType }
                              : a,
                          ),
                        )
                      }
                      sx={{ minWidth: 200 }}
                      size="small"
                    >
                      {actionTypes.map((option) => (
                        <MenuItem key={option.value} value={option.value}>
                          {option.label}
                        </MenuItem>
                      ))}
                    </TextField>

                    {(action.type === 'NotifyUser' || action.type === 'NotifyGroup') && (
                      <TextField
                        select
                        label="Notify"
                        value={action.recipient ?? 'Requester'}
                        onChange={(event) =>
                          setActions((current) =>
                            current.map((a, i) =>
                              i === index
                                ? { ...a, recipient: event.target.value as WorkflowRecipient }
                                : a,
                            ),
                          )
                        }
                        sx={{ minWidth: 200 }}
                        size="small"
                      >
                        {recipients.map((option) => (
                          <MenuItem key={option.value} value={option.value}>
                            {option.label}
                          </MenuItem>
                        ))}
                      </TextField>
                    )}

                    {needsGroup(action) && (
                      <TextField
                        select
                        label="Group"
                        value={action.targetGroupId ?? ''}
                        onChange={(event) =>
                          setActions((current) =>
                            current.map((a, i) =>
                              i === index ? { ...a, targetGroupId: event.target.value } : a,
                            ),
                          )
                        }
                        sx={{ minWidth: 220 }}
                        size="small"
                      >
                        {(groups.data ?? []).map((group) => (
                          <MenuItem key={group.id} value={group.id}>
                            {group.name}
                          </MenuItem>
                        ))}
                      </TextField>
                    )}

                    {action.type === 'SetPriority' && (
                      <TextField
                        select
                        label="Raise to"
                        value={action.targetPriority ?? ''}
                        onChange={(event) =>
                          setActions((current) =>
                            current.map((a, i) =>
                              i === index
                                ? { ...a, targetPriority: event.target.value as Priority }
                                : a,
                            ),
                          )
                        }
                        helperText="A rule can raise priority, never lower it."
                        sx={{ minWidth: 200 }}
                        size="small"
                      >
                        {priorities.map((option) => (
                          <MenuItem key={option.value} value={option.value}>
                            {option.label}
                          </MenuItem>
                        ))}
                      </TextField>
                    )}

                    {(action.type === 'NotifyUser' || action.type === 'NotifyGroup') && (
                      <TextField
                        label="Message"
                        value={action.message ?? ''}
                        onChange={(event) =>
                          setActions((current) =>
                            current.map((a, i) =>
                              i === index ? { ...a, message: event.target.value } : a,
                            ),
                          )
                        }
                        helperText="{number} and {title} are replaced. Nothing else is."
                        sx={{ flex: 1, minWidth: 240 }}
                        size="small"
                      />
                    )}

                    <IconButton
                      aria-label="Remove action"
                      onClick={() => setActions((current) => current.filter((_, i) => i !== index))}
                    >
                      <DeleteOutlineIcon fontSize="small" />
                    </IconButton>
                  </Stack>
                </Box>
              ))}
            </Stack>

            {actions.length === 0 && (
              <Alert severity="warning">
                A rule with no actions cannot be activated: it would record a run against every
                matching record and do nothing.
              </Alert>
            )}
          </CardContent>
        </Card>
      </Stack>

      <Alert severity="info" sx={{ mt: 3 }}>
        Rules act on records as people change them. A rule cannot run a script, call an external
        service, or fire another rule — see the automation notes in the documentation for why.
      </Alert>
    </Box>
  );
}

/** Whether this action needs a group picked, either as its target or as its recipient. */
function needsGroup(action: UpsertWorkflowActionInput): boolean {
  return (
    action.type === 'AssignToGroup' ||
    action.type === 'RequestApproval' ||
    ((action.type === 'NotifyUser' || action.type === 'NotifyGroup') &&
      action.recipient === 'SpecificGroup')
  );
}
