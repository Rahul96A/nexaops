import { api } from './client';
import type { PagedResult, TenantDetail, TenantOnboardingResult, TenantStatus, TenantSummary } from './types';

export interface TenantSearchParams {
  search?: string;
  status?: TenantStatus;
  page?: number;
  pageSize?: number;
}

export interface OnboardTenantInput {
  code: string;
  name: string;
  legalName?: string | null;
  primaryDomain?: string | null;
  status: TenantStatus;
  administratorEmail: string;
  administratorFirstName: string;
  administratorLastName: string;
  administratorJobTitle?: string | null;
}

export interface UpdateTenantInput {
  name: string;
  legalName?: string | null;
  primaryDomain?: string | null;
  entraTenantId?: string | null;
  recordRetentionDays: number;
  auditRetentionDays: number;
}

/**
 * Platform administration: the service provider's own view of their customers.
 *
 * Nothing here is reachable by a customer. The endpoints demand `platform.*` permissions, which
 * live only on a platform-scoped role that a tenant administrator cannot assign — so a customer
 * calling these directly gets 403 regardless of what this client sends.
 */
export const platformApi = {
  tenants: (params: TenantSearchParams, signal?: AbortSignal) =>
    api.get<PagedResult<TenantSummary>>(
      '/platform/tenants',
      params as unknown as Record<string, unknown>,
      signal,
    ),

  tenant: (id: string, signal?: AbortSignal) =>
    api.get<TenantDetail>(`/platform/tenants/${id}`, undefined, signal),

  onboard: (input: OnboardTenantInput) =>
    api.post<TenantOnboardingResult>('/platform/tenants', input),

  update: (id: string, input: UpdateTenantInput) =>
    api.put<TenantDetail>(`/platform/tenants/${id}`, input),

  setStatus: (id: string, status: TenantStatus, reason?: string | null) =>
    api.post<TenantDetail>(`/platform/tenants/${id}/status`, { status, reason }),
};
