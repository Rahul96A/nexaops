import { useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Box, Tab, Tabs } from '@mui/material';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { useAuth } from '@/auth/useAuth';
import { PageHeader } from '@/components/PageHeader';
import { EmptyState } from '@/components/EmptyState';
import { PeopleSettings } from './PeopleSettings';
import { RoleSettings } from './RoleSettings';
import { GroupSettings } from './GroupSettings';
import { CategorySettings } from './CategorySettings';

type SettingsTab = 'people' | 'roles' | 'groups' | 'categories';

/**
 * Tenant configuration.
 *
 * This is the module that decides whether a customer can run NexaOps without a developer. Every
 * screen here writes through the same permission-checked API the rest of the product uses —
 * there is no back door and no "administration mode" that bypasses tenant isolation.
 */
export function SettingsPage() {
  const [params, setParams] = useSearchParams();
  const { hasPermission } = useAuth();

  const tab = (params.get('tab') as SettingsTab) ?? 'people';

  const setTab = useCallback(
    (value: SettingsTab) => {
      const next = new URLSearchParams(params);
      next.set('tab', value);
      setParams(next, { replace: true });
    },
    [params, setParams],
  );

  const canSeeAnything =
    hasPermission(Permissions.userRead) ||
    hasPermission(Permissions.roleRead) ||
    hasPermission(Permissions.groupRead) ||
    hasPermission(Permissions.categoryRead);

  return (
    <Box>
      <PageHeader
        title="Settings"
        subtitle="People, roles, teams and the taxonomy work is classified by."
      />

      {!canSeeAnything && (
        <EmptyState
          title="Nothing to configure"
          description="Your account does not hold any of the administration permissions."
        />
      )}

      <Tabs
        value={tab}
        onChange={(_, value: SettingsTab) => setTab(value)}
        sx={{ mb: 2 }}
        variant="scrollable"
        allowScrollButtonsMobile
      >
        <Can permission={Permissions.userRead}>
          <Tab label="People" value="people" />
        </Can>
        <Can permission={Permissions.roleRead}>
          <Tab label="Roles" value="roles" />
        </Can>
        <Can permission={Permissions.groupRead}>
          <Tab label="Groups" value="groups" />
        </Can>
        <Can permission={Permissions.categoryRead}>
          <Tab label="Categories" value="categories" />
        </Can>
      </Tabs>

      {tab === 'people' && <Can permission={Permissions.userRead}><PeopleSettings /></Can>}
      {tab === 'roles' && <Can permission={Permissions.roleRead}><RoleSettings /></Can>}
      {tab === 'groups' && <Can permission={Permissions.groupRead}><GroupSettings /></Can>}
      {tab === 'categories' && (
        <Can permission={Permissions.categoryRead}>
          <CategorySettings />
        </Can>
      )}
    </Box>
  );
}
