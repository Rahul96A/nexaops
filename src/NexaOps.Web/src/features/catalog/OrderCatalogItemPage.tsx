import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Checkbox,
  Chip,
  Divider,
  FormControlLabel,
  LinearProgress,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import HowToRegOutlinedIcon from '@mui/icons-material/HowToRegOutlined';
import { catalogApi, requestsApi } from '@/api/requests';
import { ApiError } from '@/api/client';
import type { CatalogVariable } from '@/api/types';
import { PageHeader } from '@/components/PageHeader';
import { ErrorState } from '@/components/ErrorState';
import { formatCurrency } from '@/utils/format';

/**
 * The order form for one catalogue item.
 *
 * The fields are built from the item's own definition, so a catalogue change takes effect here
 * with no code change. Client-side checks are a convenience: the server validates every answer
 * against the same definition and is the actual control.
 */
export function OrderCatalogItemPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();

  const [values, setValues] = useState<Record<string, string>>({});
  const [quantity, setQuantity] = useState(1);
  const [touched, setTouched] = useState(false);

  const { data: item, isLoading, error, refetch } = useQuery({
    queryKey: ['catalog-item', id],
    queryFn: ({ signal }) => catalogApi.get(id, signal),
    enabled: Boolean(id),
  });

  const order = useMutation({
    mutationFn: () =>
      requestsApi.create({
        items: [{ catalogItemId: id, quantity, values }],
      }),
    onSuccess: (created) => navigate(`/requests/${created.id}`),
  });

  const fieldErrors = useMemo(() => {
    if (order.error instanceof ApiError) {
      return order.error.fieldErrors;
    }
    return {};
  }, [order.error]);

  if (isLoading) {
    return <LinearProgress />;
  }

  if (error) {
    return <ErrorState error={error} onRetry={() => void refetch()} />;
  }

  if (!item) {
    return null;
  }

  const setValue = (key: string, value: string) =>
    setValues((current) => ({ ...current, [key]: value }));

  const errorFor = (variable: CatalogVariable) => {
    const serverError = fieldErrors[`items[0].values.${variable.key}`]?.[0];
    if (serverError) {
      return serverError;
    }

    if (touched && variable.isRequired && !values[variable.key]?.trim()) {
      return `${variable.label} is required.`;
    }

    return undefined;
  };

  const lineTotal = item.cost != null ? item.cost * quantity : null;

  return (
    <Box>
      <PageHeader
        title={item.name}
        subtitle={item.shortDescription}
        crumbs={[{ label: 'Service catalogue', to: '/catalog' }, { label: item.name }]}
      />

      <Stack direction={{ xs: 'column', md: 'row' }} gap={3} alignItems="flex-start">
        <Card variant="outlined" sx={{ flex: 1, width: '100%' }}>
          <CardContent>
            {item.description && (
              <>
                <Typography variant="body2" color="text.secondary" sx={{ whiteSpace: 'pre-line' }}>
                  {item.description}
                </Typography>
                <Divider sx={{ my: 2 }} />
              </>
            )}

            <Stack gap={2.5}>
              <TextField
                label="Quantity"
                type="number"
                value={quantity}
                onChange={(event) => setQuantity(Math.max(1, Number(event.target.value) || 1))}
                slotProps={{ htmlInput: { min: 1, max: item.maxQuantity ?? undefined } }}
                error={Boolean(fieldErrors['items[0].quantity'])}
                helperText={
                  fieldErrors['items[0].quantity']?.[0] ??
                  (item.maxQuantity != null ? `Up to ${item.maxQuantity} per order.` : undefined)
                }
                sx={{ maxWidth: 200 }}
              />

              {item.variables.map((variable) => (
                <VariableField
                  key={variable.id}
                  variable={variable}
                  value={values[variable.key] ?? variable.defaultValue ?? ''}
                  onChange={(value) => setValue(variable.key, value)}
                  error={errorFor(variable)}
                />
              ))}
            </Stack>

            {order.error && !(order.error instanceof ApiError && order.error.isValidationError) && (
              <Alert severity="error" sx={{ mt: 2 }}>
                {order.error instanceof ApiError ? order.error.userMessage : 'Could not place the order.'}
              </Alert>
            )}

            <Stack direction="row" gap={1.5} sx={{ mt: 3 }}>
              <Button
                variant="contained"
                disabled={order.isPending}
                onClick={() => {
                  setTouched(true);
                  order.mutate();
                }}
              >
                {order.isPending ? 'Submitting…' : 'Submit request'}
              </Button>

              <Button variant="text" onClick={() => navigate('/catalog')}>
                Cancel
              </Button>
            </Stack>
          </CardContent>
        </Card>

        <Card variant="outlined" sx={{ width: { xs: '100%', md: 300 }, flexShrink: 0 }}>
          <CardContent>
            <Typography variant="overline" color="text.secondary">
              Summary
            </Typography>

            <Stack gap={1.25} sx={{ mt: 1 }}>
              {lineTotal != null && (
                <Stack direction="row" justifyContent="space-between">
                  <Typography variant="body2">Indicative cost</Typography>
                  <Typography variant="body2" fontWeight={600}>
                    {formatCurrency(lineTotal)}
                  </Typography>
                </Stack>
              )}

              {item.estimatedDeliveryDays != null && (
                <Stack direction="row" justifyContent="space-between">
                  <Typography variant="body2">Typical delivery</Typography>
                  <Typography variant="body2">{item.estimatedDeliveryDays} working days</Typography>
                </Stack>
              )}

              {item.fulfilmentGroupName && (
                <Stack direction="row" justifyContent="space-between">
                  <Typography variant="body2">Fulfilled by</Typography>
                  <Typography variant="body2">{item.fulfilmentGroupName}</Typography>
                </Stack>
              )}
            </Stack>

            {item.requiresApproval && (
              <Alert
                icon={<HowToRegOutlinedIcon fontSize="small" />}
                severity="info"
                sx={{ mt: 2 }}
              >
                Needs approval from {item.approverName ?? 'an approver'} before work starts.
              </Alert>
            )}

            {lineTotal != null && (
              <Chip
                label="Indicative only — nothing is billed from this"
                size="small"
                variant="outlined"
                sx={{ mt: 2 }}
              />
            )}
          </CardContent>
        </Card>
      </Stack>
    </Box>
  );
}

function VariableField({
  variable,
  value,
  onChange,
  error,
}: {
  variable: CatalogVariable;
  value: string;
  onChange: (value: string) => void;
  error?: string;
}) {
  const common = {
    label: variable.label,
    helperText: error ?? variable.helpText ?? undefined,
    error: Boolean(error),
    required: variable.isRequired,
    fullWidth: true,
  };

  switch (variable.type) {
    case 'Choice':
      return (
        <TextField {...common} select value={value} onChange={(e) => onChange(e.target.value)}>
          {variable.choices.map((choice) => (
            <MenuItem key={choice} value={choice}>
              {choice}
            </MenuItem>
          ))}
        </TextField>
      );

    case 'MultiChoice':
      return (
        <TextField
          {...common}
          select
          slotProps={{ select: { multiple: true } }}
          value={safeParseArray(value)}
          onChange={(event) => {
            const selected = event.target.value as unknown as string[];
            onChange(JSON.stringify(selected));
          }}
        >
          {variable.choices.map((choice) => (
            <MenuItem key={choice} value={choice}>
              {choice}
            </MenuItem>
          ))}
        </TextField>
      );

    case 'Boolean':
      return (
        <FormControlLabel
          control={
            <Checkbox
              checked={value === 'true'}
              onChange={(event) => onChange(String(event.target.checked))}
            />
          }
          label={variable.label}
        />
      );

    case 'Number':
      return (
        <TextField
          {...common}
          type="number"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          slotProps={{
            htmlInput: { min: variable.minValue ?? undefined, max: variable.maxValue ?? undefined },
          }}
        />
      );

    case 'Date':
      return (
        <TextField
          {...common}
          type="date"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          slotProps={{ inputLabel: { shrink: true } }}
        />
      );

    case 'TextArea':
      return (
        <TextField
          {...common}
          multiline
          minRows={3}
          value={value}
          onChange={(e) => onChange(e.target.value)}
        />
      );

    default:
      return (
        <TextField
          {...common}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          slotProps={{ htmlInput: { maxLength: variable.maxLength ?? undefined } }}
        />
      );
  }
}

/** A malformed stored value must not break the form; an empty selection is the safe reading. */
function safeParseArray(value: string): string[] {
  if (!value) {
    return [];
  }

  try {
    const parsed: unknown = JSON.parse(value);
    return Array.isArray(parsed) ? (parsed as string[]) : [];
  } catch {
    return [];
  }
}
