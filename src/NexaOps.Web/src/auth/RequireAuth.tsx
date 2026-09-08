import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { Box, CircularProgress } from '@mui/material';
import { useAuth } from './useAuth';

/**
 * Gates a route behind authentication and, optionally, a permission.
 *
 * A user who lacks the permission is sent to the dashboard rather than shown an error page:
 * they arrived at a link they should not have been offered, and the useful response is to put
 * them somewhere they can work.
 */
export function RequireAuth({
  children,
  permission,
}: {
  children: ReactNode;
  permission?: string;
}) {
  const { isAuthenticated, isInitialising, hasPermission } = useAuth();
  const location = useLocation();

  // Waiting for the initial profile call. Rendering the sign-in page here would flash it in
  // front of an already-signed-in user on every reload.
  if (isInitialising) {
    return (
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          minHeight: '100vh',
        }}
      >
        <CircularProgress aria-label="Loading NexaOps" />
      </Box>
    );
  }

  if (!isAuthenticated) {
    // The attempted location is preserved so sign-in can return the user to it.
    return <Navigate to="/sign-in" state={{ from: location }} replace />;
  }

  if (permission && !hasPermission(permission)) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}
