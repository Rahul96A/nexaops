import { api } from './client';
import type {
  CategoryAdmin,
  CreatedUser,
  GroupAdmin,
  GroupType,
  PagedResult,
  PermissionInfo,
  RoleDetail,
  ServiceModule,
  UserAdmin,
  UserListItem,
  UserStatus,
} from './types';

export interface UserSearchParams {
  search?: string;
  status?: UserStatus;
  roleId?: string;
  groupId?: string;
  page?: number;
  pageSize?: number;
}

export interface UpsertUserInput {
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string | null;
  employeeId?: string | null;
  jobTitle?: string | null;
  departmentId?: string | null;
  managerId?: string | null;
  location?: string | null;
  rowVersion?: string | null;
}

export interface UpsertRoleInput {
  code?: string;
  name: string;
  description?: string | null;
  permissions: string[];
  rowVersion?: string | null;
}

export interface UpsertCategoryInput {
  code?: string;
  name: string;
  description?: string | null;
  module: ServiceModule;
  defaultAssignmentGroupId?: string | null;
  sortOrder?: number;
  isActive: boolean;
  rowVersion?: string | null;
}

export interface UpsertGroupInput {
  code?: string;
  name: string;
  description?: string | null;
  type: GroupType;
  email?: string | null;
  managerUserId?: string | null;
  defaultAssigneeUserId?: string | null;
  isActive: boolean;
  rowVersion?: string | null;
}

export const adminApi = {
  users: (params: UserSearchParams, signal?: AbortSignal) =>
    api.get<PagedResult<UserListItem>>(
      '/admin/users',
      params as unknown as Record<string, unknown>,
      signal,
    ),

  user: (id: string, signal?: AbortSignal) =>
    api.get<UserAdmin>(`/admin/users/${id}`, undefined, signal),

  createUser: (input: UpsertUserInput) => api.post<CreatedUser>('/admin/users', input),

  updateUser: (id: string, input: UpsertUserInput) =>
    api.put<UserAdmin>(`/admin/users/${id}`, input),

  setUserRoles: (id: string, roleIds: string[]) =>
    api.put<UserAdmin>(`/admin/users/${id}/roles`, { roleIds }),

  setUserStatus: (id: string, status: UserStatus) =>
    api.post<UserAdmin>(`/admin/users/${id}/status`, { status }),

  roles: (signal?: AbortSignal) => api.get<RoleDetail[]>('/admin/roles', undefined, signal),

  permissions: (signal?: AbortSignal) =>
    api.get<PermissionInfo[]>('/admin/permissions', undefined, signal),

  createRole: (input: UpsertRoleInput) => api.post<RoleDetail>('/admin/roles', input),

  updateRole: (id: string, input: UpsertRoleInput) =>
    api.put<RoleDetail>(`/admin/roles/${id}`, input),

  deleteRole: (id: string) => api.delete<void>(`/admin/roles/${id}`),

  categories: (module?: ServiceModule, signal?: AbortSignal) =>
    api.get<CategoryAdmin[]>('/admin/categories', module ? { module } : undefined, signal),

  createCategory: (input: UpsertCategoryInput) =>
    api.post<CategoryAdmin>('/admin/categories', input),

  updateCategory: (id: string, input: UpsertCategoryInput) =>
    api.put<CategoryAdmin>(`/admin/categories/${id}`, input),

  deleteCategory: (id: string) => api.delete<void>(`/admin/categories/${id}`),

  groups: (signal?: AbortSignal) => api.get<GroupAdmin[]>('/admin/groups', undefined, signal),

  createGroup: (input: UpsertGroupInput) => api.post<GroupAdmin>('/admin/groups', input),

  updateGroup: (id: string, input: UpsertGroupInput) =>
    api.put<GroupAdmin>(`/admin/groups/${id}`, input),

  setGroupMember: (groupId: string, userId: string, isLead: boolean) =>
    api.put<GroupAdmin>(`/admin/groups/${groupId}/members`, { userId, isLead }),

  removeGroupMember: (groupId: string, userId: string) =>
    api.delete<GroupAdmin>(`/admin/groups/${groupId}/members/${userId}`),
};
