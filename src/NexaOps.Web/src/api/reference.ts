import { api } from './client';
import type {
  Category,
  GroupSummary,
  IndianState,
  PriorityMatrixEntry,
  UserSummary,
} from './types';

/** Look-up data the forms need. All of it is tenant-scoped by the server. */
export const referenceApi = {
  categories: (module = 'Incident', signal?: AbortSignal) =>
    api.get<Category[]>('/reference/categories', { module }, signal),

  groups: (type = 'Assignment', signal?: AbortSignal) =>
    api.get<GroupSummary[]>('/reference/groups', { type }, signal),

  myGroups: (signal?: AbortSignal) => api.get<GroupSummary[]>('/reference/my-groups', undefined, signal),

  groupMembers: (groupId: string, signal?: AbortSignal) =>
    api.get<UserSummary[]>(`/reference/groups/${groupId}/members`, undefined, signal),

  searchUsers: (search: string, signal?: AbortSignal) =>
    api.get<UserSummary[]>('/reference/users', { search }, signal),

  priorityMatrix: (signal?: AbortSignal) =>
    api.get<PriorityMatrixEntry[]>('/reference/priority-matrix', undefined, signal),

  indianStates: (signal?: AbortSignal) =>
    api.get<IndianState[]>('/reference/india/states', undefined, signal),
};
