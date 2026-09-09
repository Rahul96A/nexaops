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

// ---------------------------------------------------------------------------
// Knowledge base
// ---------------------------------------------------------------------------

export type ArticleStatus = 'Draft' | 'InReview' | 'Published' | 'Stale' | 'Retired';

export type ArticleAudience = 'Everyone' | 'ServiceDesk';

export interface ArticleSummary {
  id: string;
  number: string;
  title: string;
  summary: string;
  status: ArticleStatus;
  audience: ArticleAudience;
  categoryId?: string | null;
  categoryName?: string | null;
  authorId: string;
  authorName: string;
  publishedAt?: string | null;
  reviewDueAt?: string | null;
  viewCount: number;
  /** Null when nobody has rated it — not zero, which would condemn every new article. */
  helpfulRatio?: number | null;
  createdAt: string;
}

export interface ArticleDetail extends ArticleSummary {
  body: string;
  keywords?: string | null;
  reviewerId?: string | null;
  reviewerName?: string | null;
  problemId?: string | null;
  problemNumber?: string | null;
  submittedForReviewAt?: string | null;
  retiredAt?: string | null;
  retirementReason?: string | null;
  helpfulCount: number;
  notHelpfulCount: number;
  /** The reader's own verdict, so the UI can show which button they pressed. */
  myFeedback?: boolean | null;
  allowedTransitions: ArticleStatus[];
  rowVersion?: string | null;
}

export interface KnowledgeSummaryCounts {
  published: number;
  stale: number;
  inReview: number;
  drafts: number;
  myDrafts: number;
  totalViews: number;
}

export interface ArticleSearchParams {
  search?: string;
  status?: string;
  scope?: string;
  sortBy?: string;
  sortDescending?: boolean;
  page?: number;
  pageSize?: number;
}

// ---------------------------------------------------------------------------
// CMDB
// ---------------------------------------------------------------------------

export type CiType =
  | 'Server' | 'VirtualMachine' | 'NetworkDevice' | 'StorageDevice'
  | 'Database' | 'Application' | 'Middleware'
  | 'BusinessService' | 'TechnicalService'
  | 'Workstation' | 'MobileDevice' | 'Printer'
  | 'SoftwareLicence' | 'CloudResource';

export type CiStatus = 'Planned' | 'Operational' | 'Impaired' | 'Retired' | 'Disposed';

export type CiCriticality = 'Low' | 'Medium' | 'High' | 'Critical';

export type CiRelationshipType =
  | 'DependsOn' | 'Contains' | 'RunsOn' | 'ConnectsTo' | 'FailsOverTo';

export interface CiSummary {
  id: string;
  number: string;
  name: string;
  type: CiType;
  status: CiStatus;
  criticality: CiCriticality;
  environment?: string | null;
  location?: string | null;
  ownerUserId?: string | null;
  ownerName?: string | null;
  supportGroupId?: string | null;
  supportGroupName?: string | null;
  supportExpiresOn?: string | null;
  isOutOfSupport: boolean;
  createdAt: string;
}

export interface RelatedItem {
  id: string;
  number: string;
  name: string;
  type: CiType;
  status: CiStatus;
  criticality: CiCriticality;
  relationship: CiRelationshipType;
  /** Hops from the item. 1 is a direct neighbour. */
  depth: number;
}

export interface CiDetail {
  id: string;
  number: string;
  name: string;
  description?: string | null;
  type: CiType;
  status: CiStatus;
  criticality: CiCriticality;
  location?: string | null;
  serialNumber?: string | null;
  manufacturer?: string | null;
  model?: string | null;
  version?: string | null;
  environment?: string | null;
  ownerUserId?: string | null;
  ownerName?: string | null;
  supportGroupId?: string | null;
  supportGroupName?: string | null;
  acquiredOn?: string | null;
  supportExpiresOn?: string | null;
  isOutOfSupport: boolean;
  vendor?: string | null;
  createdAt: string;
  impacts: RelatedItem[];
  dependsOn: RelatedItem[];
  openIncidentCount: number;
  rowVersion?: string | null;
}

export interface CmdbSummaryCounts {
  totalItems: number;
  operational: number;
  impaired: number;
  criticalItems: number;
  outOfSupport: number;
  unowned: number;
  byType: { type: CiType; count: number }[];
}

// ---------------------------------------------------------------------------
// Assets
// ---------------------------------------------------------------------------

export type AssetKind = 'Hardware' | 'Software' | 'Peripheral' | 'Mobile' | 'Consumable';

export type AssetStatus =
  | 'OnOrder' | 'InStock' | 'Assigned' | 'InRepair' | 'Retired' | 'Disposed' | 'Lost';

export type LicenceModel = 'PerUser' | 'PerDevice' | 'Concurrent' | 'SiteLicence';

export type ComplianceState = 'UnderUsed' | 'Compliant' | 'OverDeployed' | 'Expired';

export interface AssetSummary {
  id: string;
  number: string;
  assetTag: string;
  name: string;
  kind: AssetKind;
  status: AssetStatus;
  manufacturer?: string | null;
  model?: string | null;
  serialNumber?: string | null;
  assignedToUserId?: string | null;
  assignedToName?: string | null;
  location?: string | null;
  warrantyExpiresOn?: string | null;
  isOutOfWarranty: boolean;
  refreshDueOn?: string | null;
  isDueForRefresh: boolean;
  purchaseCost?: number | null;
  createdAt: string;
}

export interface AssetCustody {
  id: string;
  userId: string;
  userName: string;
  assignedAt: string;
  returnedAt?: string | null;
  assignmentNote?: string | null;
  returnNote?: string | null;
}

export interface AssetDetail extends AssetSummary {
  assignedAt?: string | null;
  purchasedOn?: string | null;
  vendor?: string | null;
  purchaseOrderNumber?: string | null;
  usefulLifeMonths?: number | null;
  configurationItemId?: string | null;
  configurationItemName?: string | null;
  disposedOn?: string | null;
  disposalNotes?: string | null;
  custodyHistory: AssetCustody[];
  rowVersion?: string | null;
}

export interface LicenceSummary {
  id: string;
  number: string;
  productName: string;
  publisher?: string | null;
  version?: string | null;
  model: LicenceModel;
  entitlementCount: number;
  deployedCount: number;
  availableEntitlements?: number | null;
  overDeployedBy: number;
  compliance: ComplianceState;
  expiresOn?: string | null;
  annualCost?: number | null;
  vendor?: string | null;
  agreementReference?: string | null;
  notes?: string | null;
  createdAt: string;
}

export interface AssetSummaryCounts {
  totalAssets: number;
  inStock: number;
  assigned: number;
  inRepair: number;
  dueForRefresh: number;
  outOfWarranty: number;
  totalPurchaseCost?: number | null;
  licences: number;
  overDeployedLicences: number;
  expiredLicences: number;
  overDeployedSeats: number;
  /** Indicative only — real remediation is negotiated, not arithmetic. */
  exposureCost?: number | null;
}

// --- Workflow automation ---

export type WorkflowTrigger =
  | 'RecordCreated' | 'StatusChanged' | 'PriorityChanged' | 'AssignmentChanged';

export type WorkflowActionType =
  | 'NotifyUser' | 'NotifyGroup' | 'AssignToGroup' | 'AssignToUser'
  | 'SetPriority' | 'RequestApproval';

export type WorkflowRecipient =
  | 'Requester' | 'Assignee' | 'AssignmentGroup' | 'SpecificUser' | 'SpecificGroup';

export type WorkflowConditionOperator =
  | 'Equals' | 'NotEquals' | 'In' | 'GreaterThan' | 'LessThan'
  | 'IsEmpty' | 'IsNotEmpty' | 'Contains';

export type WorkflowRunStatus =
  | 'Skipped' | 'Succeeded' | 'PartiallyCompleted' | 'Failed' | 'Suppressed';

export type WorkflowStepStatus = 'Succeeded' | 'Skipped' | 'Failed';

/** The modules the engine is driven from. Others cannot carry rules. */
export type WorkflowModule = 'Incident' | 'Request' | 'Problem' | 'Change';

export interface WorkflowCondition {
  id: string;
  field: string;
  operator: WorkflowConditionOperator;
  value?: string | null;
}

export interface WorkflowAction {
  id: string;
  sequence: number;
  type: WorkflowActionType;
  recipient?: WorkflowRecipient | null;
  targetGroupId?: string | null;
  targetGroupName?: string | null;
  targetUserId?: string | null;
  targetUserName?: string | null;
  targetPriority?: Priority | null;
  message?: string | null;
}

export interface WorkflowSummary {
  id: string;
  name: string;
  description?: string | null;
  module: WorkflowModule;
  trigger: WorkflowTrigger;
  isActive: boolean;
  sequence: number;
  conditionCount: number;
  actionCount: number;
  lastRunAt?: string | null;
  runCount: number;
  createdAt: string;
}

export interface WorkflowDetail {
  id: string;
  name: string;
  description?: string | null;
  module: WorkflowModule;
  trigger: WorkflowTrigger;
  isActive: boolean;
  sequence: number;
  conditions: WorkflowCondition[];
  actions: WorkflowAction[];
  lastRunAt?: string | null;
  runCount: number;
  createdAt: string;
  rowVersion?: string | null;
}

export interface WorkflowStepRun {
  sequence: number;
  actionType: WorkflowActionType;
  status: WorkflowStepStatus;
  detail?: string | null;
  error?: string | null;
}

export interface WorkflowRun {
  id: string;
  workflowDefinitionId: string;
  workflowName: string;
  module: WorkflowModule;
  recordId: string;
  recordNumber: string;
  trigger: WorkflowTrigger;
  status: WorkflowRunStatus;
  startedAt: string;
  completedAt?: string | null;
  outcome?: string | null;
  steps: WorkflowStepRun[];
}

/** A field a rule may be written against, published by the server so the editor cannot drift. */
export interface WorkflowField {
  field: string;
  label: string;
  hint?: string | null;
}

export interface UpsertWorkflowConditionInput {
  field: string;
  operator: WorkflowConditionOperator;
  value?: string | null;
}

export interface UpsertWorkflowActionInput {
  sequence: number;
  type: WorkflowActionType;
  recipient?: WorkflowRecipient | null;
  targetGroupId?: string | null;
  targetUserId?: string | null;
  targetPriority?: Priority | null;
  message?: string | null;
}

export interface UpsertWorkflowInput {
  name: string;
  description?: string | null;
  module: WorkflowModule;
  trigger: WorkflowTrigger;
  isActive: boolean;
  sequence: number;
  conditions: UpsertWorkflowConditionInput[];
  actions: UpsertWorkflowActionInput[];
  rowVersion?: string | null;
}

// --- Reporting ---

/** A measured value with what it was measured over. Percent is null when there is no denominator. */
export interface Rate {
  numerator: number;
  denominator: number;
  percent?: number | null;
}

export interface DailyVolume {
  date: string;
  created: number;
  resolved: number;
}

export interface BreakdownRow {
  label: string;
  id?: string | null;
  created: number;
  resolved: number;
  breached: number;
  breachRate: Rate;
}

/** Durations arrive as .NET TimeSpan strings ("1.02:03:04"), or null when nothing was measured. */
export interface DurationStats {
  mean?: string | null;
  median?: string | null;
  sample: number;
}

export interface ServiceDeskReport {
  from: string;
  to: string;
  isPartialPeriod: boolean;
  created: number;
  resolved: number;
  reopened: number;
  stillOpen: number;
  slaAttainment: Rate;
  reopenRate: Rate;
  timeToResolve: DurationStats;
  daily: DailyVolume[];
  byPriority: BreakdownRow[];
  byCategory: BreakdownRow[];
  byGroup: BreakdownRow[];
}

export interface SlaAttainmentRow {
  target: 'Response' | 'Resolution' | 'Closure';
  priority?: Priority | null;
  met: number;
  breached: number;
  attainment: Rate;
}

export interface SlaReport {
  from: string;
  to: string;
  overall: Rate;
  rows: SlaAttainmentRow[];
  stillRunning: number;
  cancelled: number;
}

export interface ChangeReport {
  from: string;
  to: string;
  raised: number;
  reviewed: number;
  awaitingReview: number;
  successRate: Rate;
  emergencyShare: Rate;
  outcomes: { outcome: ChangeOutcome; count: number }[];
}

export interface RequestReport {
  from: string;
  to: string;
  raised: number;
  fulfilled: number;
  cancelled: number;
  awaitingApproval: number;
  timeToFulfil: DurationStats;
  daily: DailyVolume[];
  byCatalogItem: BreakdownRow[];
}

// --- Administration ---

/** The record types that share the platform taxonomy. Mirrors the server enum. */
export type ServiceModule =
  | 'Incident' | 'Request' | 'Problem' | 'Change'
  | 'Knowledge' | 'Asset' | 'ConfigurationItem';

export type UserStatus = 'Active' | 'Disabled' | 'Suspended' | 'Locked';

export type GroupType = 'Assignment' | 'Approval' | 'Notification' | 'Security';

export interface SubcategoryAdmin {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  defaultAssignmentGroupId?: string | null;
  defaultAssignmentGroupName?: string | null;
  sortOrder: number;
  isActive: boolean;
  recordCount: number;
}

export interface CategoryAdmin {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  module: ServiceModule;
  defaultAssignmentGroupId?: string | null;
  defaultAssignmentGroupName?: string | null;
  sortOrder: number;
  isActive: boolean;
  recordCount: number;
  subcategories: SubcategoryAdmin[];
  rowVersion?: string | null;
}

export interface GroupMemberAdmin {
  userId: string;
  displayName: string;
  email: string;
  jobTitle?: string | null;
  avatarColor?: string | null;
  isLead: boolean;
  isActive: boolean;
}

export interface GroupAdmin {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  type: GroupType;
  email?: string | null;
  managerUserId?: string | null;
  managerName?: string | null;
  defaultAssigneeUserId?: string | null;
  defaultAssigneeName?: string | null;
  businessCalendarId?: string | null;
  businessCalendarName?: string | null;
  isActive: boolean;
  memberCount: number;
  members: GroupMemberAdmin[];
  rowVersion?: string | null;
}

export interface RoleSummary {
  id: string;
  code: string;
  name: string;
  isSystem: boolean;
}

export interface RoleDetail {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  isSystem: boolean;
  userCount: number;
  permissions: string[];
  rowVersion?: string | null;
}

export interface PermissionInfo {
  code: string;
  category: string;
  name: string;
  description: string;
}

export interface UserListItem {
  id: string;
  email: string;
  displayName: string;
  jobTitle?: string | null;
  departmentName?: string | null;
  status: UserStatus;
  avatarColor?: string | null;
  lastLoginAt?: string | null;
  roleCount: number;
}

export interface UserAdmin {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  displayName: string;
  phoneNumber?: string | null;
  employeeId?: string | null;
  jobTitle?: string | null;
  organizationId?: string | null;
  organizationName?: string | null;
  departmentId?: string | null;
  departmentName?: string | null;
  managerId?: string | null;
  managerName?: string | null;
  location?: string | null;
  timeZoneId?: string | null;
  locale?: string | null;
  status: UserStatus;
  isServiceAccount: boolean;
  mustChangePassword: boolean;
  lastLoginAt?: string | null;
  roles: RoleSummary[];
  groups: string[];
  rowVersion?: string | null;
}

/** Returned once, on creation. The password is never retrievable afterwards. */
export interface CreatedUser {
  user: UserAdmin;
  temporaryPassword?: string | null;
}

// --- Virtual agent ---

export type VirtualAgentActionKind = 'raise_incident';

/** A suggestion, not an action. Nothing exists until the person confirms it. */
export interface VirtualAgentProposal {
  kind: VirtualAgentActionKind;
  title: string;
  description: string;
  urgency: Urgency;
}

export interface VirtualAgentArticle {
  id: string;
  number: string;
  title: string;
}

export interface VirtualAgentReply {
  reply: string;
  articles: VirtualAgentArticle[];
  proposal?: VirtualAgentProposal | null;
  toolsUsed: string[];
}

export interface VirtualAgentActionResult {
  recordNumber: string;
  recordId: string;
}

// ---------------------------------------------------------------------------
// Platform administration
// ---------------------------------------------------------------------------

export type TenantStatus = 'Active' | 'Trial' | 'Suspended' | 'Closed';

export interface TenantSummary {
  id: string;
  code: string;
  name: string;
  legalName?: string | null;
  status: TenantStatus;
  primaryDomain?: string | null;
  dataRegion: string;
  createdAt: string;
  userCount: number;
  activeUserCount: number;
}

export interface TenantDetail {
  id: string;
  code: string;
  name: string;
  legalName?: string | null;
  status: TenantStatus;
  primaryDomain?: string | null;
  entraTenantId?: string | null;
  timeZoneId: string;
  locale: string;
  currencyCode: string;
  dateFormat: string;
  dataRegion: string;
  recordRetentionDays: number;
  auditRetentionDays: number;
  createdAt: string;
  userCount: number;
  activeUserCount: number;
  lastSignInAt?: string | null;
}

/**
 * The result of onboarding. `temporaryPassword` is present exactly once, in the response to the
 * request that created the tenant, and is null under federated authentication.
 */
export interface TenantOnboardingResult {
  tenant: TenantDetail;
  administratorUserId: string;
  administratorEmail: string;
  temporaryPassword?: string | null;
}
