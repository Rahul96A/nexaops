import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Alert, Button, Card, CardContent, Stack, TextField, Typography } from '@mui/material';
import { authApi } from '@/api/auth';
import { ApiError } from '@/api/client';
import { PageHeader } from '@/components/PageHeader';

/**
 * Mirrors the server minimum. The server remains the authority; this exists so a user finds
 * out before submitting rather than after.
 */
const schema = z
  .object({
    currentPassword: z.string().min(1, 'Enter your current password.'),
    newPassword: z.string().min(12, 'Use at least 12 characters.'),
    confirmPassword: z.string().min(1, 'Confirm your new password.'),
  })
  .refine((values) => values.newPassword === values.confirmPassword, {
    message: 'The two passwords do not match.',
    path: ['confirmPassword'],
  });

type Values = z.infer<typeof schema>;

export function ChangePasswordPage() {
  const [status, setStatus] = useState<'idle' | 'saved' | 'error'>('idle');
  const [message, setMessage] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<Values>({ resolver: zodResolver(schema) });

  async function onSubmit(values: Values) {
    setStatus('idle');
    setMessage(null);

    try {
      await authApi.changePassword(values.currentPassword, values.newPassword);
      setStatus('saved');
      reset();
    } catch (error) {
      setStatus('error');
      setMessage(
        error instanceof ApiError ? error.userMessage : 'The password could not be changed.',
      );
    }
  }

  return (
    <>
      <PageHeader
        title="Change password"
        subtitle="Changing your password signs you out of every other session."
      />

      <Card sx={{ maxWidth: 520 }}>
        <CardContent sx={{ p: 3 }}>
          {status === 'saved' && (
            <Alert severity="success" sx={{ mb: 2 }}>
              Your password has been changed. Other sessions have been signed out.
            </Alert>
          )}

          {status === 'error' && message && (
            <Alert severity="error" sx={{ mb: 2 }}>
              {message}
            </Alert>
          )}

          <form onSubmit={handleSubmit(onSubmit)} noValidate>
            <Stack spacing={2}>
              <TextField
                {...register('currentPassword')}
                type="password"
                label="Current password"
                autoComplete="current-password"
                error={Boolean(errors.currentPassword)}
                helperText={errors.currentPassword?.message}
                fullWidth
              />

              <TextField
                {...register('newPassword')}
                type="password"
                label="New password"
                autoComplete="new-password"
                error={Boolean(errors.newPassword)}
                helperText={errors.newPassword?.message ?? 'At least 12 characters.'}
                fullWidth
              />

              <TextField
                {...register('confirmPassword')}
                type="password"
                label="Confirm new password"
                autoComplete="new-password"
                error={Boolean(errors.confirmPassword)}
                helperText={errors.confirmPassword?.message}
                fullWidth
              />

              <Typography variant="caption" color="text.secondary">
                Passwords are stored only as a PBKDF2 hash. Nobody at NexaOps can read yours.
              </Typography>

              <Button type="submit" variant="contained" disabled={isSubmitting}>
                {isSubmitting ? 'Saving…' : 'Change password'}
              </Button>
            </Stack>
          </form>
        </CardContent>
      </Card>
    </>
  );
}
