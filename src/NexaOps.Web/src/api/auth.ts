import { api, tokenStore } from './client';
import type { AuthenticationResult, SignInRequest, UserProfile } from './types';

/** Authentication and the signed-in user's profile. */
export const authApi = {
  async signIn(request: SignInRequest): Promise<AuthenticationResult> {
    const result = await api.post<AuthenticationResult>('/auth/sign-in', request, {
      // A failure here must surface as a sign-in failure, never as a token refresh attempt.
      skipAuthRefresh: true,
    });

    tokenStore.set(result);
    return result;
  },

  async signOut(): Promise<void> {
    const refreshToken = tokenStore.getRefreshToken();

    try {
      if (refreshToken) {
        await api.post<void>('/auth/sign-out', { refreshToken }, { skipAuthRefresh: true });
      }
    } finally {
      // The local session ends regardless of whether the server could be reached.
      tokenStore.clear();
    }
  },

  profile: () => api.get<UserProfile>('/auth/me'),

  changePassword: (currentPassword: string, newPassword: string) =>
    api.post<void>('/auth/change-password', { currentPassword, newPassword }),
};
