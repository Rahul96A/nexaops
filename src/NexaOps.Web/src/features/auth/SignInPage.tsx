import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useLocation, useNavigate } from 'react-router-dom';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Divider,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import LoginIcon from '@mui/icons-material/Login';
import { ApiError } from '@/api/client';
import type { TenantChoice } from '@/api/types';
import { useAuth } from '@/auth/useAuth';

const schema = z.object({
  email: z.string().min(1, 'Enter your email address.').email('Enter a valid email address.'),
  password: z.string().min(1, 'Enter your password.'),
  tenantCode: z.string().optional(),
});

type SignInValues = z.infer<typeof schema>;

/**
 * Sign-in.
 *
 * The failure message is deliberately identical for every cause - unknown account, wrong
 * password, locked account - because the server treats them identically. Saying "no such user"
 * would turn this form into an account enumeration tool.
 */
export function SignInPage() {
  const { signIn } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const [formError, setFormError] = useState<string | null>(null);
  const [tenantChoices, setTenantChoices] = useState<TenantChoice[]>([]);

  const {
    register,
    handleSubmit,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<SignInValues>({
    resolver: zodResolver(schema),
    defaultValues: { email: '', password: '', tenantCode: '' },
  });

  const returnTo = (location.state as { from?: { pathname: string } } | null)?.from?.pathname ?? '/';

  async function onSubmit(values: SignInValues) {
    setFormError(null);

    try {
      await signIn(values.email.trim(), values.password, values.tenantCode || undefined);
      navigate(returnTo, { replace: true });
    } catch (error) {
      if (error instanceof ApiError) {
        // The same address exists in more than one tenant, so the user has to say which.
        if (error.code === 'tenant_selection_required' && error.problem.tenants) {
          setTenantChoices(error.problem.tenants);
          setFormError('This email address is used by more than one organization. Choose yours to continue.');
          return;
        }

        setFormError(error.userMessage);
        return;
      }

      setFormError('Could not reach the NexaOps service. Check your connection and try again.');
    }
  }

  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'grid',
        gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' },
        bgcolor: 'background.default',
      }}
    >
      {/* Brand panel. Hidden on small screens, where the form should own the viewport. */}
      <Box
        sx={{
          display: { xs: 'none', md: 'flex' },
          flexDirection: 'column',
          justifyContent: 'space-between',
          p: 6,
          color: '#fff',
          background: 'linear-gradient(150deg, #0A2CA8 0%, #1B4DFF 45%, #7A3BFF 100%)',
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
          <Box
            aria-hidden
            sx={{
              width: 38,
              height: 38,
              borderRadius: '10px',
              bgcolor: 'rgba(255,255,255,0.16)',
              display: 'grid',
              placeItems: 'center',
              fontWeight: 800,
              fontSize: 19,
            }}
          >
            N
          </Box>
          <Typography variant="h3" component="span">
            NexaOps
          </Typography>
        </Box>

        <Box sx={{ maxWidth: 460 }}>
          <Typography variant="h1" sx={{ fontSize: '2.25rem', mb: 2, lineHeight: 1.2 }}>
            AI-native service management for Indian enterprises
          </Typography>

          <Typography sx={{ opacity: 0.9, mb: 4 }}>
            Incidents, SLAs and service operations in one platform - built on Azure, with tenant
            isolation and a full audit trail from the first release.
          </Typography>

          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            {['Multi-tenant', 'IST business calendars', 'Audit trail', 'Azure-native'].map((tag) => (
              <Chip
                key={tag}
                label={tag}
                size="small"
                sx={{ bgcolor: 'rgba(255,255,255,0.16)', color: '#fff' }}
              />
            ))}
          </Stack>
        </Box>

        <Typography variant="caption" sx={{ opacity: 0.7 }}>
          NexaOps is an independent product. It is not affiliated with, or derived from, any
          other IT service management vendor.
        </Typography>
      </Box>

      {/* Form panel */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', p: { xs: 3, md: 6 } }}>
        <Card sx={{ width: '100%', maxWidth: 420 }}>
          <CardContent sx={{ p: 4 }}>
            <Typography variant="h2" component="h1" gutterBottom>
              Sign in
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
              Use your work account to continue.
            </Typography>

            {formError && (
              <Alert severity="error" sx={{ mb: 2 }}>
                {formError}
              </Alert>
            )}

            <Box component="form" onSubmit={handleSubmit(onSubmit)} noValidate>
              <Stack spacing={2}>
                <TextField
                  {...register('email')}
                  label="Email address"
                  type="email"
                  autoComplete="username"
                  autoFocus
                  fullWidth
                  error={Boolean(errors.email)}
                  helperText={errors.email?.message}
                />

                <TextField
                  {...register('password')}
                  label="Password"
                  type="password"
                  autoComplete="current-password"
                  fullWidth
                  error={Boolean(errors.password)}
                  helperText={errors.password?.message}
                />

                {tenantChoices.length > 0 && (
                  <TextField
                    {...register('tenantCode')}
                    select
                    label="Organization"
                    fullWidth
                    defaultValue=""
                    onChange={(event) => setValue('tenantCode', event.target.value)}
                  >
                    {tenantChoices.map((choice) => (
                      <MenuItem key={choice.code} value={choice.code}>
                        {choice.name}
                      </MenuItem>
                    ))}
                  </TextField>
                )}

                <Button
                  type="submit"
                  variant="contained"
                  size="large"
                  disabled={isSubmitting}
                  startIcon={<LoginIcon />}
                  fullWidth
                >
                  {isSubmitting ? 'Signing in…' : 'Sign in'}
                </Button>
              </Stack>
            </Box>

            <Divider sx={{ my: 3 }}>
              <Typography variant="caption" color="text.secondary">
                Single sign-on
              </Typography>
            </Divider>

            <Typography variant="caption" color="text.secondary" display="block" textAlign="center">
              Microsoft Entra ID sign-in is configured per tenant. When it is enabled for your
              organization, this page redirects to Microsoft automatically.
            </Typography>
          </CardContent>
        </Card>
      </Box>
    </Box>
  );
}
