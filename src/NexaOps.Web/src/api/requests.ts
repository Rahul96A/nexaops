import { api } from './client';
import type {
  Approval,
  CatalogItemDetail,
  CatalogItemSummary,
  CreateRequestRequest,
  DecideApprovalRequest,
  PagedResult,
  RequestComment,
  RequestDetail,
  RequestSearchParams,
  RequestSummary,
  RequestSummaryCounts,
  RequestStatus,
} from './types';

export const requestsApi = {
  search: (params: RequestSearchParams, signal?: AbortSignal) =>
    api.get<PagedResult<RequestSummary>>(
      '/requests',
      params as unknown as Record<string, unknown>,
      signal,
    ),

  summary: (signal?: AbortSignal) =>
    api.get<RequestSummaryCounts>('/requests/summary', undefined, signal),

  get: (id: string, signal?: AbortSignal) =>
    api.get<RequestDetail>(`/requests/${id}`, undefined, signal),

  create: (request: CreateRequestRequest) => api.post<RequestDetail>('/requests', request),

  assign: (id: string, request: { fulfilmentGroupId?: string | null; assignedToUserId?: string | null }) =>
    api.post<RequestDetail>(`/requests/${id}/assign`, request),

  changeStatus: (id: string, request: { status: RequestStatus; pendingReason?: string; note?: string }) =>
    api.post<RequestDetail>(`/requests/${id}/status`, request),

  fulfilItem: (id: string, itemId: string, request: { notes?: string }) =>
    api.post<RequestDetail>(`/requests/${id}/items/${itemId}/fulfil`, request),

  cancel: (id: string, request: { reason?: string }) =>
    api.post<RequestDetail>(`/requests/${id}/cancel`, request),

  comments: (id: string, signal?: AbortSignal) =>
    api.get<RequestComment[]>(`/requests/${id}/comments`, undefined, signal),

  addComment: (id: string, request: { body: string; kind: 'PublicComment' | 'WorkNote' }) =>
    api.post<RequestComment>(`/requests/${id}/comments`, request),
};

export const catalogApi = {
  browse: (params: { search?: string; categoryId?: string }, signal?: AbortSignal) =>
    api.get<CatalogItemSummary[]>(
      '/catalog/items',
      params as unknown as Record<string, unknown>,
      signal,
    ),

  get: (id: string, signal?: AbortSignal) =>
    api.get<CatalogItemDetail>(`/catalog/items/${id}`, undefined, signal),
};

export const approvalsApi = {
  mine: (outstandingOnly: boolean, signal?: AbortSignal) =>
    api.get<Approval[]>('/approvals', { outstandingOnly }, signal),

  decide: (id: string, request: DecideApprovalRequest) =>
    api.post<RequestDetail>(`/approvals/${id}/decide`, request),
};
