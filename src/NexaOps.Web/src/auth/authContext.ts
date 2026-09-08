import { createContext } from 'react';
import type { UserProfile } from '@/api/types';

/**
 * The signed-in user, and the permissions the UI uses to decide what to show.
 *
 * The permission list here is a convenience for hiding actions the user cannot take. It is not
 * a security boundary: the server checks every operation independently, so a user who forces a
 * hidden button into view still gets a 403.
 */
export interface AuthState {
  profile: UserProfile | null;
  isAuthenticated: boolean;
  /** True until the initial profile fetch settles, so the app can avoid flashing the sign-in page. */
  isInitialising: boolean;
  signIn: (email: string, password: string, tenantCode?: string) => Promise<void>;
  signOut: () => Promise<void>;
  refreshProfile: () => Promise<void>;
  hasPermission: (permission: string) => boolean;
  hasAnyPermission: (...permissions: string[]) => boolean;
}

export const AuthContext = createContext<AuthState | null>(null);
