import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { authApi } from '@/api/auth';
import { ApiError, setSessionExpiredHandler, tokenStore } from '@/api/client';
import type { UserProfile } from '@/api/types';
import { AuthContext, type AuthState } from './authContext';

export function AuthProvider({ children }: { children: ReactNode }) {
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [isInitialising, setInitialising] = useState(true);
  const queryClient = useQueryClient();

  const permissions = useMemo(() => new Set(profile?.permissions ?? []), [profile]);

  const clearSession = useCallback(() => {
    tokenStore.clear();
    setProfile(null);

    // Cached responses belong to the identity that fetched them. Without this, signing in as a
    // different person on the same browser renders the previous user's data from cache until
    // each query refetches - which showed a requester unpublished catalogue items that the API
    // had correctly withheld from them.
    queryClient.clear();
  }, [queryClient]);

  // The HTTP client cannot navigate, so it reports an unrecoverable 401 here instead. Without
  // this the app would sit on a page quietly failing every request.
  useEffect(() => {
    setSessionExpiredHandler(clearSession);
  }, [clearSession]);

  // Restore the session on load. A stored token is only trusted as far as the server confirms
  // it: the profile call is what actually validates it.
  useEffect(() => {
    let cancelled = false;

    async function restore() {
      if (!tokenStore.getAccessToken()) {
        setInitialising(false);
        return;
      }

      try {
        const restored = await authApi.profile();
        if (!cancelled) {
          setProfile(restored);
        }
      } catch (error) {
        if (!cancelled && error instanceof ApiError && error.isUnauthorized) {
          clearSession();
        }
      } finally {
        if (!cancelled) {
          setInitialising(false);
        }
      }
    }

    void restore();

    return () => {
      cancelled = true;
    };
  }, [clearSession]);

  const signIn = useCallback(
    async (email: string, password: string, tenantCode?: string) => {
      // Cleared before the new identity is established, so nothing from the previous session
      // can be read even for the moment before the first refetch lands.
      queryClient.clear();

      const result = await authApi.signIn({ email, password, tenantCode });
      setProfile(result.profile);
    },
    [queryClient],
  );

  const signOut = useCallback(async () => {
    try {
      await authApi.signOut();
    } finally {
      setProfile(null);
      queryClient.clear();
    }
  }, [queryClient]);

  const refreshProfile = useCallback(async () => {
    setProfile(await authApi.profile());
  }, []);

  const hasPermission = useCallback(
    (permission: string) => permissions.has(permission),
    [permissions],
  );

  const hasAnyPermission = useCallback(
    (...required: string[]) => required.some((p) => permissions.has(p)),
    [permissions],
  );

  const value = useMemo<AuthState>(
    () => ({
      profile,
      isAuthenticated: profile !== null,
      isInitialising,
      signIn,
      signOut,
      refreshProfile,
      hasPermission,
      hasAnyPermission,
    }),
    [profile, isInitialising, signIn, signOut, refreshProfile, hasPermission, hasAnyPermission],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
