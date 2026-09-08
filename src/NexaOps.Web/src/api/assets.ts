import { api } from './client';
import type {
  AssetDetail,
  AssetStatus,
  AssetSummary,
  AssetSummaryCounts,
  LicenceSummary,
  PagedResult,
} from './types';

export interface AssetSearchParams {
  search?: string;
  status?: string;
  kind?: string;
  scope?: string;
  sortBy?: string;
  sortDescending?: boolean;
  page?: number;
  pageSize?: number;
}

export const assetsApi = {
  search: (params: AssetSearchParams, signal?: AbortSignal) =>
    api.get<PagedResult<AssetSummary>>(
      '/assets',
      params as unknown as Record<string, unknown>,
      signal,
    ),

  summary: (signal?: AbortSignal) =>
    api.get<AssetSummaryCounts>('/assets/summary', undefined, signal),

  get: (id: string, signal?: AbortSignal) =>
    api.get<AssetDetail>(`/assets/${id}`, undefined, signal),

  assign: (id: string, request: { userId: string; note?: string }) =>
    api.post<AssetDetail>(`/assets/${id}/assign`, request),

  return: (id: string, request: { note?: string; returnTo?: AssetStatus }) =>
    api.post<AssetDetail>(`/assets/${id}/return`, request),

  licences: (signal?: AbortSignal) =>
    api.get<LicenceSummary[]>('/assets/licences', undefined, signal),
};
