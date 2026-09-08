import { api } from './client';
import type { ChangeReport, RequestReport, ServiceDeskReport, SlaReport } from './types';

export interface ReportPeriodParams {
  from: string;
  to: string;
  groupId?: string;
}

export const reportsApi = {
  serviceDesk: (params: ReportPeriodParams, signal?: AbortSignal) =>
    api.get<ServiceDeskReport>(
      '/reports/service-desk',
      params as unknown as Record<string, unknown>,
      signal,
    ),

  sla: (params: ReportPeriodParams, signal?: AbortSignal) =>
    api.get<SlaReport>('/reports/sla', params as unknown as Record<string, unknown>, signal),

  changes: (params: ReportPeriodParams, signal?: AbortSignal) =>
    api.get<ChangeReport>('/reports/changes', params as unknown as Record<string, unknown>, signal),

  requests: (params: ReportPeriodParams, signal?: AbortSignal) =>
    api.get<RequestReport>(
      '/reports/requests',
      params as unknown as Record<string, unknown>,
      signal,
    ),

  /**
   * The URL a download link points at.
   *
   * Built rather than fetched: the browser has to make the request itself for the file to land
   * in the downloads folder, and the API's cookie-less bearer auth means this only works because
   * the export endpoint is reached through the same authenticated fetch below.
   */
  exportPath: (report: string, params: ReportPeriodParams) => {
    const query = new URLSearchParams({ from: params.from, to: params.to });

    if (params.groupId) {
      query.set('groupId', params.groupId);
    }

    return `/reports/${report}/export?${query.toString()}`;
  },
};
