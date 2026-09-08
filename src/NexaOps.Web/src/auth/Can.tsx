import type { ReactNode } from 'react';
import { useAuth } from './useAuth';

/**
 * Renders its children only when the user holds the permission.
 *
 * This is presentation only. Hiding a button does not protect the operation behind it - the
 * API enforces the same permission independently - it just avoids offering an action that
 * would fail.
 */
export function Can({
  permission,
  anyOf,
  fallback = null,
  children,
}: {
  permission?: string;
  anyOf?: string[];
  fallback?: ReactNode;
  children: ReactNode;
}) {
  const { hasPermission, hasAnyPermission } = useAuth();

  const allowed = permission
    ? hasPermission(permission)
    : anyOf
      ? hasAnyPermission(...anyOf)
      : true;

  return <>{allowed ? children : fallback}</>;
}
