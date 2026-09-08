import { api } from './client';
import type {
  PagedResult,
  UpsertWorkflowInput,
  WorkflowDetail,
  WorkflowField,
  WorkflowModule,
  WorkflowRun,
  WorkflowRunStatus,
  WorkflowSummary,
  WorkflowTrigger,
} from './types';

export interface WorkflowSearchParams {
  search?: string;
  module?: WorkflowModule;
  trigger?: WorkflowTrigger;
  isActive?: boolean;
  page?: number;
  pageSize?: number;
}

export interface WorkflowRunSearchParams {
  workflowDefinitionId?: string;
  recordId?: string;
  status?: WorkflowRunStatus;
  page?: number;
  pageSize?: number;
}

export const workflowsApi = {
  search: (params: WorkflowSearchParams, signal?: AbortSignal) =>
    api.get<PagedResult<WorkflowSummary>>(
      '/workflows',
      params as unknown as Record<string, unknown>,
      signal,
    ),

  get: (id: string, signal?: AbortSignal) =>
    api.get<WorkflowDetail>(`/workflows/${id}`, undefined, signal),

  /**
   * The fields a rule may test, for one module.
   *
   * Fetched rather than hard-coded here: the server validates against its own list, so a copy in
   * the client would eventually offer something the API refuses.
   */
  fields: (module: WorkflowModule, signal?: AbortSignal) =>
    api.get<WorkflowField[]>('/workflows/fields', { module }, signal),

  runs: (params: WorkflowRunSearchParams, signal?: AbortSignal) =>
    api.get<PagedResult<WorkflowRun>>(
      '/workflows/runs',
      params as unknown as Record<string, unknown>,
      signal,
    ),

  create: (input: UpsertWorkflowInput) => api.post<WorkflowDetail>('/workflows', input),

  update: (id: string, input: UpsertWorkflowInput) =>
    api.put<WorkflowDetail>(`/workflows/${id}`, input),

  setActive: (id: string, isActive: boolean) =>
    api.post<WorkflowDetail>(`/workflows/${id}/active`, { isActive }),
};
