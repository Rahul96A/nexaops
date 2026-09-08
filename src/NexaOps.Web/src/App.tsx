import { useEffect, useMemo, useState } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { CssBaseline, ThemeProvider } from '@mui/material';
import { AppShell } from '@/components/AppShell';
import { RequireAuth } from '@/auth/RequireAuth';
import { Permissions } from '@/auth/permissions';
import { useAuth } from '@/auth/useAuth';
import { darkTheme, lightTheme } from '@/theme/theme';
import { configureFormatting } from '@/utils/format';
import { SignInPage } from '@/features/auth/SignInPage';
import { ChangePasswordPage } from '@/features/auth/ChangePasswordPage';
import { ServiceDeskPage } from '@/features/dashboard/ServiceDeskPage';
import { IncidentListPage } from '@/features/incidents/IncidentListPage';
import { IncidentDetailPage } from '@/features/incidents/IncidentDetailPage';
import { NewIncidentPage } from '@/features/incidents/NewIncidentPage';
import { MyWorkPage } from '@/features/incidents/MyWorkPage';
import { CatalogPage } from '@/features/catalog/CatalogPage';
import { OrderCatalogItemPage } from '@/features/catalog/OrderCatalogItemPage';
import { RequestListPage } from '@/features/requests/RequestListPage';
import { RequestDetailPage } from '@/features/requests/RequestDetailPage';
import { ApprovalsPage } from '@/features/approvals/ApprovalsPage';
import { ProblemListPage } from '@/features/problems/ProblemListPage';
import { ProblemDetailPage } from '@/features/problems/ProblemDetailPage';
import { ChangeListPage } from '@/features/changes/ChangeListPage';
import { ChangeDetailPage } from '@/features/changes/ChangeDetailPage';
import { AuditPage } from '@/features/audit/AuditPage';
import { NotFoundPage } from '@/features/NotFoundPage';

const COLOR_MODE_KEY = 'nexaops.colorMode';

export function App() {
  const { profile } = useAuth();
  const [colorMode, setColorMode] = useState<'light' | 'dark'>(readStoredColorMode);

  // Timestamps and currency render in the tenant's timezone and locale, so this has to be
  // applied as soon as the profile is known and re-applied if the user switches tenant.
  useEffect(() => {
    configureFormatting(profile?.timeZoneId, profile?.locale);
  }, [profile?.timeZoneId, profile?.locale]);

  useEffect(() => {
    try {
      window.localStorage.setItem(COLOR_MODE_KEY, colorMode);
    } catch {
      // A blocked storage API only means the preference does not survive a reload.
    }
  }, [colorMode]);

  const theme = useMemo(() => (colorMode === 'dark' ? darkTheme : lightTheme), [colorMode]);

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />

      <Routes>
        <Route path="/sign-in" element={<SignInPage />} />

        <Route
          element={
            <RequireAuth>
              <AppShell
                colorMode={colorMode}
                onToggleColorMode={() => setColorMode((mode) => (mode === 'light' ? 'dark' : 'light'))}
              />
            </RequireAuth>
          }
        >
          <Route index element={<ServiceDeskPage />} />
          <Route path="my-work" element={<MyWorkPage />} />

          <Route
            path="incidents"
            element={
              <RequireAuth permission={Permissions.incidentRead}>
                <IncidentListPage />
              </RequireAuth>
            }
          />

          <Route
            path="incidents/new"
            element={
              <RequireAuth permission={Permissions.incidentCreate}>
                <NewIncidentPage />
              </RequireAuth>
            }
          />

          <Route
            path="incidents/:id"
            element={
              <RequireAuth permission={Permissions.incidentRead}>
                <IncidentDetailPage />
              </RequireAuth>
            }
          />

          <Route
            path="catalog"
            element={
              <RequireAuth permission={Permissions.catalogRead}>
                <CatalogPage />
              </RequireAuth>
            }
          />

          <Route
            path="catalog/:id"
            element={
              <RequireAuth permission={Permissions.catalogRead}>
                <OrderCatalogItemPage />
              </RequireAuth>
            }
          />

          <Route
            path="requests"
            element={
              <RequireAuth permission={Permissions.requestRead}>
                <RequestListPage />
              </RequireAuth>
            }
          />

          <Route
            path="requests/:id"
            element={
              <RequireAuth permission={Permissions.requestRead}>
                <RequestDetailPage />
              </RequireAuth>
            }
          />

          <Route
            path="approvals"
            element={
              <RequireAuth permission={Permissions.approvalAct}>
                <ApprovalsPage />
              </RequireAuth>
            }
          />

          <Route
            path="problems"
            element={
              <RequireAuth permission={Permissions.problemRead}>
                <ProblemListPage />
              </RequireAuth>
            }
          />

          <Route
            path="problems/:id"
            element={
              <RequireAuth permission={Permissions.problemRead}>
                <ProblemDetailPage />
              </RequireAuth>
            }
          />

          <Route
            path="changes"
            element={
              <RequireAuth permission={Permissions.changeRead}>
                <ChangeListPage />
              </RequireAuth>
            }
          />

          <Route
            path="changes/:id"
            element={
              <RequireAuth permission={Permissions.changeRead}>
                <ChangeDetailPage />
              </RequireAuth>
            }
          />

          <Route
            path="audit"
            element={
              <RequireAuth permission={Permissions.auditRead}>
                <AuditPage />
              </RequireAuth>
            }
          />

          <Route path="change-password" element={<ChangePasswordPage />} />

          <Route path="404" element={<NotFoundPage />} />
          <Route path="*" element={<Navigate to="/404" replace />} />
        </Route>
      </Routes>
    </ThemeProvider>
  );
}

function readStoredColorMode(): 'light' | 'dark' {
  try {
    const stored = window.localStorage.getItem(COLOR_MODE_KEY);
    if (stored === 'light' || stored === 'dark') {
      return stored;
    }
  } catch {
    // Fall through to the system preference.
  }

  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}
