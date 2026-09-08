/**
 * The wire contract with the NexaOps API.
 *
 * These mirror the server DTOs. Enums are transmitted as names rather than numbers, so a
 * reordered enum on the server cannot silently change the meaning of a stored value here.
 */

// ---------------------------------------------------------------------------
// Shared
// ---------------------------------------------------------------------------

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

export type SortDirection = 'Ascending' | 'Descending';

/** RFC 9457 problem details, as returned by every API failure. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  /** Stable machine-readable identifier, e.g. `incident.invalid_transition`. */
  code?: string;
  correlationId?: string;
  /** Present on validation failures: field name to messages. */
  errors?: Record<string, string[]>;
  /** Present when sign-in matched several tenants. */
  tenants?: TenantChoice[];
}

export interface TenantChoice {
  code: string;
  name: string;
}

// ---------------------------------------------------------------------------
// Identity
// ---------------------------------------------------------------------------

export interface SignInRequest {
  email: string;
  password: string;
  tenantCode?: string;
}

export interface AuthenticationResult {
  accessToken: string;
  expiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  profile: UserProfile;
}

export interface GroupMembership {
  groupId: string;
  name: string;
  isLead: boolean;
}

export interface UserProfile {
  userId: string;
  email: string;
  displayName: string;
  firstName: string;
  lastName: string;
  jobTitle?: string | null;
  avatarColor?: string | null;
  tenantId: string;
  tenantCode: string;
  tenantName: string;
  timeZoneId: string;
  locale: string;
  currencyCode: string;
  dateFormat: string;
  organizationId?: string | null;
  organizationName?: string | null;
  departmentId?: string | null;
  departmentName?: string | null;
  roles: string[];
  permissions: string[];
  groups: GroupMembership[];
  mustChangePassword: boolean;
  isPlatformAdministrator: boolean;
}

// ---------------------------------------------------------------------------
// Incidents
// ---------------------------------------------------------------------------

export type Impact = 'Extensive' | 'Significant' | 'Moderate' | 'Minor';

export type Urgency = 'Critical' | 'High' | 'Medium' | 'Low';

export type Priority = 'P1Critical' | 'P2High' | 'P3Moderate' | 'P4Low' | 'P5Planning';

export type IncidentStatus =
  | 'New'
  | 'Assigned'
  | 'InProgress'
  | 'Pending'
  | 'Resolved'
  | 'Closed'
  | 'Cancelled';

export type PendingReason =
  | 'AwaitingRequester'
  | 'AwaitingVendor'
  | 'AwaitingChange'
  | 'AwaitingParts'
  | 'AwaitingProblem';

export type IncidentChannel =
  | 'Portal'
  | 'Email'
  | 'Phone'
  | 'Chat'
  | 'WalkIn'
  | 'Monitoring'
  | 'Api'
  | 'AiAssistant';

export type ResolutionCode =
  | 'Resolved'
  | 'ResolvedByWorkaround'
  | 'ResolvedByKnownError'
  | 'ResolvedByChange'
  | 'Duplicate'
  | 'NoFaultFound'
  | 'UserEducated'
  | 'WithdrawnByRequester';

export type IncidentCommentKind = 'PublicComment' | 'WorkNote';

export type SlaTargetType = 'Response' | 'Resolution' | 'Closure';

export type SlaState = 'InProgress' | 'Paused' | 'Met' | 'Breached' | 'Cancelled';

export type IncidentViewScope =
  | 'All'
  | 'AssignedToMe'
  | 'MyTeam'
  | 'RaisedByMe'
  | 'Unassigned'
  | 'Breached'
  | 'DueSoon';

export interface SlaInstance {
  id: string;
  name: string;
  targetType: SlaTargetType;
  state: SlaState;
  startedAt: string;
  dueAt: string;
  completedAt?: string | null;
  breachedAt?: string | null;
  durationMinutes: number;
  elapsedMinutes: number;
  /** Negative once the commitment is overrun. */
  remainingMinutes: number;
  consumedPercent: number;
  warningThresholdPercent: number;
}

export interface IncidentListItem {
  id: string;
  number: string;
  title: string;
  status: IncidentStatus;
  priority: Priority;
  impact: Impact;
  urgency: Urgency;
  categoryId?: string | null;
  categoryName?: string | null;
  subcategoryName?: string | null;
  requesterId: string;
  requesterName: string;
  assignedToUserId?: string | null;
  assignedToName?: string | null;
  assignmentGroupId?: string | null;
  assignmentGroupName?: string | null;
  channel: IncidentChannel;
  isMajorIncident: boolean;
  hasBreachedSla: boolean;
  nextSlaDueAt?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  resolvedAt?: string | null;
}

export interface IncidentDetail {
  id: string;
  number: string;
  title: string;
  description: string;
  status: IncidentStatus;
  pendingReason?: PendingReason | null;
  impact: Impact;
  urgency: Urgency;
  priority: Priority;
  isPriorityOverridden: boolean;
  priorityOverrideReason?: string | null;
  channel: IncidentChannel;
  isMajorIncident: boolean;
  requesterId: string;
  requesterName: string;
  requesterEmail?: string | null;
  affectedUserId?: string | null;
  affectedUserName?: string | null;
  organizationId?: string | null;
  organizationName?: string | null;
  departmentId?: string | null;
  departmentName?: string | null;
  categoryId?: string | null;
  categoryName?: string | null;
  subcategoryId?: string | null;
  subcategoryName?: string | null;
  assignmentGroupId?: string | null;
  assignmentGroupName?: string | null;
  assignedToUserId?: string | null;
  assignedToName?: string | null;
  resolutionCode?: ResolutionCode | null;
  resolutionNotes?: string | null;
  firstRespondedAt?: string | null;
  resolvedAt?: string | null;
  resolvedByName?: string | null;
  closedAt?: string | null;
  reopenCount: number;
  parentIncidentId?: string | null;
  parentIncidentNumber?: string | null;
  createdAt: string;
  createdByName?: string | null;
  updatedAt?: string | null;
  updatedByName?: string | null;
  tags: string[];
  slaInstances: SlaInstance[];
  /** Statuses this incident may legally move to, so the UI need not duplicate the rules. */
  allowedTransitions: IncidentStatus[];
  attachmentCount: number;
  commentCount: number;
  /** Round-trip this on update so a concurrent edit is rejected rather than overwritten. */
  concurrencyToken?: string | null;
}

export interface IncidentComment {
  id: string;
  kind: IncidentCommentKind;
  body: string;
  authorUserId: string;
  authorName: string;
  authorAvatarColor?: string | null;
  isSystemGenerated: boolean;
  createdAt: string;
}

export interface IncidentFieldChange {
  field: string;
  from?: string | null;
  to?: string | null;
}

export interface IncidentActivity {
  id: string;
  type: 'comment' | 'work_note' | 'field_change';
  occurredAt: string;
  actorUserId?: string | null;
  actorName?: string | null;
  actorAvatarColor?: string | null;
  body?: string | null;
  commentKind?: IncidentCommentKind | null;
  isSystemGenerated: boolean;
  changes?: IncidentFieldChange[] | null;
}

export interface AgentWorkload {
  userId: string;
  displayName: string;
  avatarColor?: string | null;
  openCount: number;
  breachedCount: number;
}

export interface ServiceDeskSummary {
  openIncidents: number;
  criticalOpen: number;
  highOpen: number;
  unassignedInMyGroups: number;
  assignedToMe: number;
  raisedByMe: number;
  breachedOpen: number;
  dueWithinTwoHours: number;
  resolvedToday: number;
  createdToday: number;
  teamWorkload: AgentWorkload[];
  openByPriority: { priority: Priority; count: number }[];
  openByStatus: { status: IncidentStatus; count: number }[];
}

export interface IncidentSearchParams {
  search?: string;
  scope?: IncidentViewScope;
  status?: IncidentStatus[];
  priority?: Priority[];
  openOnly?: boolean;
  assignedToUserId?: string;
  assignmentGroupId?: string;
  requesterId?: string;
  categoryId?: string;
  subcategoryId?: string;
  isMajorIncident?: boolean;
  hasBreachedSla?: boolean;
  createdFrom?: string;
  createdTo?: string;
  tag?: string;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: SortDirection;
}

export interface CreateIncidentRequest {
  title: string;
  description: string;
  requesterId?: string | null;
  affectedUserId?: string | null;
  categoryId?: string | null;
  subcategoryId?: string | null;
  impact: Impact;
  urgency: Urgency;
  assignmentGroupId?: string | null;
  assignedToUserId?: string | null;
  channel?: IncidentChannel;
  tags?: string[];
}

export interface UpdateIncidentRequest {
  title?: string;
  description?: string;
  categoryId?: string | null;
  subcategoryId?: string | null;
  impact?: Impact;
  urgency?: Urgency;
  affectedUserId?: string | null;
  tags?: string[];
  concurrencyToken?: string | null;
}

export interface AssignIncidentRequest {
  assignmentGroupId?: string | null;
  assignedToUserId?: string | null;
  note?: string;
}

export interface ChangeStatusRequest {
  status: IncidentStatus;
  pendingReason?: PendingReason;
  resolutionCode?: ResolutionCode;
  notes?: string;
}

export interface ChangePriorityRequest {
  impact?: Impact;
  urgency?: Urgency;
  overridePriority?: Priority;
  overrideReason?: string;
}

export interface AddCommentRequest {
  body: string;
  kind: IncidentCommentKind;
}

// ---------------------------------------------------------------------------
// Reference data
// ---------------------------------------------------------------------------

export interface Subcategory {
  id: string;
  code: string;
  name: string;
  defaultAssignmentGroupId?: string | null;
}

export interface Category {
  id: string;
  code: string;
  name: string;
  defaultAssignmentGroupId?: string | null;
  subcategories: Subcategory[];
}

export interface GroupSummary {
  id: string;
  code: string;
  name: string;
  email?: string | null;
  memberCount: number;
}

export interface UserSummary {
  id: string;
  displayName: string;
  email: string;
  jobTitle?: string | null;
  avatarColor?: string | null;
  isLead: boolean;
}

export interface PriorityMatrixEntry {
  impact: Impact;
  urgency: Urgency;
  priority: Priority;
}

export interface IndianState {
  code: string;
  name: string;
  gstStateCode: string;
  isUnionTerritory: boolean;
}

// ---------------------------------------------------------------------------
// Notifications
// ---------------------------------------------------------------------------

export type NotificationSeverity = 'Information' | 'Success' | 'Warning' | 'Critical';

export interface AppNotification {
  id: string;
  kind: string;
  severity: NotificationSeverity;
  title: string;
  body: string;
  module?: string | null;
  recordId?: string | null;
  actionUrl?: string | null;
  isRead: boolean;
  createdAt: string;
}

// ---------------------------------------------------------------------------
// Audit
// ---------------------------------------------------------------------------

export interface AuditEvent {
  id: string;
  occurredAt: string;
  actorUserId?: string | null;
  actorDisplayName?: string | null;
  action: string;
  entityType: string;
  entityId?: string | null;
  entityLabel?: string | null;
  source: string;
  outcome: string;
  changedFields?: string | null;
  beforeJson?: string | null;
  afterJson?: string | null;
  message?: string | null;
  correlationId?: string | null;
  ipAddress?: string | null;
}

// ---------------------------------------------------------------------------
// AI
// ---------------------------------------------------------------------------

export interface AiToolSummary {
  name: string;
  description: string;
  requiredPermission: string;
  isMutating: boolean;
}

export interface AiStatus {
  isConfigured: boolean;
  provider: string;
  chatModel?: string | null;
  semanticSearchAvailable: boolean;
  availableTools: AiToolSummary[];
  unavailableReason?: string | null;
}

export interface AiTurn {
  role: 'user' | 'assistant';
  content: string;
}

export interface AiAnswer {
  answer: string;
  toolsUsed: string[];
  inputTokens: number;
  outputTokens: number;
}

// ---------------------------------------------------------------------------
// Service catalogue, requests and approvals
// ---------------------------------------------------------------------------

export type CatalogItemStatus = 'Draft' | 'Published' | 'Retired';

export type VariableType =
  | 'Text'
  | 'TextArea'
  | 'Number'
  | 'Date'
  | 'Boolean'
  | 'Choice'
  | 'MultiChoice'
  | 'User'
  | 'Group';

export type RequestStatus =
  | 'Draft'
  | 'AwaitingApproval'
  | 'Approved'
  | 'InProgress'
  | 'Pending'
  | 'Fulfilled'
  | 'Closed'
  | 'Rejected'
  | 'Cancelled';

export type RequestItemStatus = 'Pending' | 'InProgress' | 'Fulfilled' | 'Cancelled';

export type ApprovalState = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled' | 'NotRequired';

export type ApprovalRule = 'Unanimous' | 'AnyOne';

export type ApprovalTargetKind = 'User' | 'Group' | 'Manager';

export interface CatalogVariable {
  id: string;
  key: string;
  label: string;
  helpText?: string | null;
  type: VariableType;
  isRequired: boolean;
  sortOrder: number;
  defaultValue?: string | null;
  choices: string[];
  maxLength?: number | null;
  minValue?: number | null;
  maxValue?: number | null;
}

export interface CatalogItemSummary {
  id: string;
  code: string;
  name: string;
  shortDescription: string;
  categoryId?: string | null;
  categoryName?: string | null;
  status: CatalogItemStatus;
  cost?: number | null;
  estimatedDeliveryDays?: number | null;
  icon?: string | null;
  requiresApproval: boolean;
  sortOrder: number;
}

export interface CatalogItemDetail extends Omit<CatalogItemSummary, 'sortOrder'> {
  description: string;
  priority: Priority;
  maxQuantity?: number | null;
  approvalTargetKind: ApprovalTargetKind;
  approverName?: string | null;
  fulfilmentGroupId?: string | null;
  fulfilmentGroupName?: string | null;
  variables: CatalogVariable[];
  rowVersion?: string | null;
}

export interface RequestItem {
  id: string;
  catalogItemId: string;
  catalogItemName: string;
  quantity: number;
  unitCost?: number | null;
  lineCost?: number | null;
  status: RequestItemStatus;
  values: Record<string, string>;
  fulfilmentGroupId?: string | null;
  fulfilmentGroupName?: string | null;
  assignedToUserId?: string | null;
  assignedToName?: string | null;
  fulfilledAt?: string | null;
  fulfilmentNotes?: string | null;
}

export interface Approval {
  id: string;
  stage: number;
  rule: ApprovalRule;
  targetKind: ApprovalTargetKind;
  approverUserId?: string | null;
  approverName?: string | null;
  approverGroupId?: string | null;
  approverGroupName?: string | null;
  state: ApprovalState;
  decidedByUserId?: string | null;
  decidedByName?: string | null;
  decidedAt?: string | null;
  comment?: string | null;
  recordLabel: string;
  module: string;
  recordId: string;
  recordNumber?: string | null;
}

export interface RequestComment {
  id: string;
  kind: IncidentCommentKind;
  body: string;
  authorId: string;
  authorDisplayName: string;
  createdAt: string;
}

export interface RequestSummary {
  id: string;
  number: string;
  title: string;
  status: RequestStatus;
  priority: Priority;
  requesterId: string;
  requesterName: string;
  requestedForId: string;
  requestedForName: string;
  assignedToUserId?: string | null;
  assignedToName?: string | null;
  assignedToAvatarColor?: string | null;
  fulfilmentGroupId?: string | null;
  fulfilmentGroupName?: string | null;
  itemCount: number;
  totalCost?: number | null;
  hasBreachedSla: boolean;
  nextSlaDueAt?: string | null;
  createdAt: string;
}

export interface RequestDetail {
  id: string;
  number: string;
  title: string;
  description: string;
  status: RequestStatus;
  pendingReason?: string | null;
  channel: string;
  priority: Priority;
  requesterId: string;
  requesterName: string;
  requestedForId: string;
  requestedForName: string;
  categoryId?: string | null;
  categoryName?: string | null;
  fulfilmentGroupId?: string | null;
  fulfilmentGroupName?: string | null;
  assignedToUserId?: string | null;
  assignedToName?: string | null;
  requiredByDate?: string | null;
  submittedAt?: string | null;
  approvedAt?: string | null;
  fulfilledAt?: string | null;
  closedAt?: string | null;
  rejectionReason?: string | null;
  cancellationReason?: string | null;
  totalCost?: number | null;
  hasBreachedSla: boolean;
  nextSlaDueAt?: string | null;
  createdAt: string;
  items: RequestItem[];
  approvals: Approval[];
  allowedTransitions: RequestStatus[];
  rowVersion?: string | null;
}

export interface RequestStatusCount {
  status: RequestStatus;
  count: number;
}

export interface RequestSummaryCounts {
  openRequests: number;
  awaitingApproval: number;
  awaitingMyApproval: number;
  unassigned: number;
  assignedToMe: number;
  raisedByMe: number;
  breachedOpen: number;
  fulfilledToday: number;
  createdToday: number;
  openByStatus: RequestStatusCount[];
}

export interface RequestLineInput {
  catalogItemId: string;
  quantity: number;
  values: Record<string, string>;
}

export interface CreateRequestRequest {
  title?: string;
  description?: string;
  requestedForId?: string | null;
  requiredByDate?: string | null;
  items: RequestLineInput[];
}

export interface RequestSearchParams {
  search?: string;
  status?: string;
  priority?: string;
  assignedToUserId?: string;
  fulfilmentGroupId?: string;
  scope?: string;
  openOnly?: boolean;
  breachedOnly?: boolean;
  sortBy?: string;
  sortDescending?: boolean;
  page?: number;
  pageSize?: number;
}

export interface DecideApprovalRequest {
  approved: boolean;
  comment?: string;
}

// ---------------------------------------------------------------------------
// Problem management
// ---------------------------------------------------------------------------

export type ProblemStatus =
  | 'New'
  | 'Investigating'
  | 'KnownError'
  | 'FixInProgress'
  | 'Resolved'
  | 'Closed'
  | 'Cancelled';

export type ProblemOrigin = 'FromIncident' | 'Proactive' | 'Vendor' | 'PostIncidentReview';

export type RootCauseConfidence = 'Suspected' | 'Probable' | 'Confirmed';

export interface ProblemSummary {
  id: string;
  number: string;
  title: string;
  status: ProblemStatus;
  priority: Priority;
  origin: ProblemOrigin;
  assignedToUserId?: string | null;
  assignedToName?: string | null;
  assignedToAvatarColor?: string | null;
  ownerUserId?: string | null;
  ownerName?: string | null;
  assignmentGroupId?: string | null;
  assignmentGroupName?: string | null;
  categoryId?: string | null;
  categoryName?: string | null;
  hasWorkaround: boolean;
  linkedIncidentCount: number;
  isMajorProblem: boolean;
  createdAt: string;
}

export interface LinkedIncident {
  id: string;
  number: string;
  title: string;
  status: IncidentStatus;
  priority: Priority;
  createdAt: string;
}

export interface ProblemComment {
  id: string;
  kind: IncidentCommentKind;
  body: string;
  authorId: string;
  authorDisplayName: string;
  createdAt: string;
}

export interface ProblemDetail {
  id: string;
  number: string;
  title: string;
  description: string;
  status: ProblemStatus;
  priority: Priority;
  origin: ProblemOrigin;
  categoryId?: string | null;
  categoryName?: string | null;
  subcategoryId?: string | null;
  subcategoryName?: string | null;
  assignmentGroupId?: string | null;
  assignmentGroupName?: string | null;
  assignedToUserId?: string | null;
  assignedToName?: string | null;
  ownerUserId?: string | null;
  ownerName?: string | null;
  rootCause?: string | null;
  rootCauseConfidence?: RootCauseConfidence | null;
  workaround?: string | null;
  permanentFix?: string | null;
  isMajorProblem: boolean;
  linkedIncidentCount: number;
  investigationStartedAt?: string | null;
  knownErrorAt?: string | null;
  resolvedAt?: string | null;
  closedAt?: string | null;
  createdAt: string;
  linkedIncidents: LinkedIncident[];
  allowedTransitions: ProblemStatus[];
  rowVersion?: string | null;
}

export interface ProblemStatusCount {
  status: ProblemStatus;
  count: number;
}

export interface ProblemSummaryCounts {
  openProblems: number;
  investigating: number;
  knownErrors: number;
  assignedToMe: number;
  ownedByMe: number;
  unassigned: number;
  majorProblems: number;
  openByStatus: ProblemStatusCount[];
}

export interface ProblemSearchParams {
  search?: string;
  status?: string;
  priority?: string;
  scope?: string;
  openOnly?: boolean;
  knownErrorsOnly?: boolean;
  sortBy?: string;
  sortDescending?: boolean;
  page?: number;
  pageSize?: number;
}

export interface CreateProblemRequest {
  title: string;
  description?: string;
  categoryId?: string | null;
  priority?: Priority;
  origin?: ProblemOrigin;
  fromIncidentId?: string | null;
}

export interface RecordFindingsRequest {
  rootCause?: string;
  confidence?: RootCauseConfidence;
  workaround?: string;
  permanentFix?: string;
}

// ---------------------------------------------------------------------------
// Change management
// ---------------------------------------------------------------------------

export type ChangeType = 'Standard' | 'Normal' | 'Emergency';

export type ChangeStatus =
  | 'Draft'
  | 'Assessing'
  | 'AwaitingApproval'
  | 'Scheduled'
  | 'Implementing'
  | 'Review'
  | 'Closed'
  | 'Rejected'
  | 'Cancelled';

export type ChangeRisk = 'Low' | 'Medium' | 'High' | 'VeryHigh';

export type ChangeOutcome = 'Successful' | 'SuccessfulWithIssues' | 'Failed' | 'RolledBack';

export interface ChangeSummary {
  id: string;
  number: string;
  title: string;
  type: ChangeType;
  status: ChangeStatus;
  risk: ChangeRisk;
  priority: Priority;
  outcome?: ChangeOutcome | null;
  assignedToUserId?: string | null;
  assignedToName?: string | null;
  assignedToAvatarColor?: string | null;
  assignmentGroupId?: string | null;
  assignmentGroupName?: string | null;
  plannedStartAt?: string | null;
  plannedEndAt?: string | null;
  requiresDowntime: boolean;
  createdAt: string;
}

export interface ChangeDetail {
  id: string;
  number: string;
  title: string;
  description: string;
  type: ChangeType;
  status: ChangeStatus;
  risk: ChangeRisk;
  impact: string;
  priority: Priority;
  implementationPlan?: string | null;
  rollbackPlan?: string | null;
  testPlan?: string | null;
  impactAssessment?: string | null;
  plannedStartAt?: string | null;
  plannedEndAt?: string | null;
  actualStartAt?: string | null;
  actualEndAt?: string | null;
  requiresDowntime: boolean;
  assignmentGroupId?: string | null;
  assignmentGroupName?: string | null;
  assignedToUserId?: string | null;
  assignedToName?: string | null;
  requestedByUserId: string;
  requestedByName: string;
  categoryId?: string | null;
  categoryName?: string | null;
  problemId?: string | null;
  problemNumber?: string | null;
  outcome?: ChangeOutcome | null;
  reviewNotes?: string | null;
  rejectionReason?: string | null;
  cancellationReason?: string | null;
  approvedAt?: string | null;
  reviewedAt?: string | null;
  closedAt?: string | null;
  createdAt: string;
  approvals: Approval[];
  collidingChanges: ChangeSummary[];
  allowedTransitions: ChangeStatus[];
  rowVersion?: string | null;
}

export interface ChangeSummaryCounts {
  openChanges: number;
  awaitingApproval: number;
  scheduledThisWeek: number;
  implementing: number;
  awaitingReview: number;
  assignedToMe: number;
  emergencyThisMonth: number;
  openByStatus: { status: ChangeStatus; count: number }[];
}

export interface ChangeSearchParams {
  search?: string;
  status?: string;
  type?: string;
  risk?: string;
  scope?: string;
  openOnly?: boolean;
  sortBy?: string;
  sortDescending?: boolean;
  page?: number;
  pageSize?: number;
}
