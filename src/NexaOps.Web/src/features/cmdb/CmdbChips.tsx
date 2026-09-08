import { Chip, Tooltip } from '@mui/material';
import type { AssetStatus, CiCriticality, CiStatus, ComplianceState } from '@/api/types';

type ChipColour = 'default' | 'primary' | 'secondary' | 'error' | 'info' | 'success' | 'warning';

const ciStatusLabels: Record<CiStatus, string> = {
  Planned: 'Planned',
  Operational: 'Operational',
  Impaired: 'Impaired',
  Retired: 'Retired',
  Disposed: 'Disposed',
};

const ciStatusColours: Record<CiStatus, ChipColour> = {
  Planned: 'info',
  Operational: 'success',
  Impaired: 'warning',
  Retired: 'default',
  Disposed: 'default',
};

export function CiStatusChip({ status }: { status: CiStatus }) {
  return (
    <Chip
      label={ciStatusLabels[status] ?? status}
      color={ciStatusColours[status] ?? 'default'}
      size="small"
      variant={status === 'Operational' || status === 'Impaired' ? 'filled' : 'outlined'}
    />
  );
}

const criticalityLabels: Record<CiCriticality, string> = {
  Low: 'Low',
  Medium: 'Medium',
  High: 'High',
  Critical: 'Critical',
};

const criticalityColours: Record<CiCriticality, ChipColour> = {
  Low: 'default',
  Medium: 'default',
  High: 'warning',

  // Critical means the business stops. It should be the thing the eye lands on in a list.
  Critical: 'error',
};

export function CriticalityChip({ criticality }: { criticality: CiCriticality }) {
  return (
    <Chip
      label={criticalityLabels[criticality] ?? criticality}
      color={criticalityColours[criticality] ?? 'default'}
      size="small"
      variant={criticality === 'Critical' ? 'filled' : 'outlined'}
    />
  );
}

const assetStatusLabels: Record<AssetStatus, string> = {
  OnOrder: 'On order',
  InStock: 'In stock',
  Assigned: 'Issued',
  InRepair: 'In repair',
  Retired: 'Retired',
  Disposed: 'Disposed',
  Lost: 'Unaccounted for',
};

const assetStatusColours: Record<AssetStatus, ChipColour> = {
  OnOrder: 'info',
  InStock: 'default',
  Assigned: 'primary',
  InRepair: 'warning',
  Retired: 'default',
  Disposed: 'default',

  // Deliberately distinct from disposed, and coloured as a problem: an unaccounted-for device
  // is a security question, not a bookkeeping one.
  Lost: 'error',
};

export function AssetStatusChip({ status }: { status: AssetStatus }) {
  return (
    <Chip
      label={assetStatusLabels[status] ?? status}
      color={assetStatusColours[status] ?? 'default'}
      size="small"
      variant={status === 'Disposed' || status === 'Retired' ? 'outlined' : 'filled'}
    />
  );
}

const complianceLabels: Record<ComplianceState, string> = {
  UnderUsed: 'Spare capacity',
  Compliant: 'Compliant',
  OverDeployed: 'Over-deployed',
  Expired: 'Expired',
};

const complianceHelp: Record<ComplianceState, string> = {
  UnderUsed: 'Fewer deployments than entitlements. Room to grow, or seats to reclaim.',
  Compliant: 'Within entitlement.',
  OverDeployed: 'More deployments than entitlements. This is what costs money in an audit.',
  Expired: 'The agreement has lapsed, so every deployment against it is unlicensed.',
};

const complianceColours: Record<ComplianceState, ChipColour> = {
  UnderUsed: 'info',
  Compliant: 'success',
  OverDeployed: 'error',
  Expired: 'error',
};

export function ComplianceChip({ compliance }: { compliance: ComplianceState }) {
  return (
    <Tooltip title={complianceHelp[compliance] ?? ''}>
      <Chip
        label={complianceLabels[compliance] ?? compliance}
        color={complianceColours[compliance] ?? 'default'}
        size="small"
        variant={compliance === 'Compliant' || compliance === 'UnderUsed' ? 'outlined' : 'filled'}
      />
    </Tooltip>
  );
}
