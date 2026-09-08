import { useContext } from 'react';
import { AuthContext, type AuthState } from './authContext';

/** The signed-in user and the permission helpers. Throws outside the provider. */
export function useAuth(): AuthState {
  const context = useContext(AuthContext);

  if (!context) {
    throw new Error('useAuth must be used inside an AuthProvider.');
  }

  return context;
}
