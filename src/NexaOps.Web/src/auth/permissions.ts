/**
 * The permission codes the UI checks.
 *
 * These mirror the server catalogue. They decide what is rendered, never what is allowed:
 * every operation is authorised again by the API, so a mismatch here is a cosmetic bug rather
 * than a security hole.
 */
export const Permissions = {
  incidentRead: 'incident.read',
  incidentReadAll: 'incident.read.all',
  incidentCreate: 'incident.create',
  incidentUpdate: 'incident.update',
  incidentAssign: 'incident.assign',
  incidentResolve: 'incident.resolve',
  incidentClose: 'incident.close',
  incidentReopen: 'incident.reopen',
  incidentCancel: 'incident.cancel',
  incidentPriorityOverride: 'incident.priority.override',
  incidentDeclareMajor: 'incident.declare_major',
  incidentCommentCreate: 'incident.comment.create',
  incidentWorkNoteRead: 'incident.worknote.read',
  incidentWorkNoteCreate: 'incident.worknote.create',
  incidentExport: 'incident.export',
  incidentArchive: 'incident.archive',

  catalogRead: 'catalog.read',
  catalogManage: 'catalog.manage',

  requestRead: 'request.read',
  requestReadAll: 'request.read.all',
  requestCreate: 'request.create',
  requestUpdate: 'request.update',
  requestAssign: 'request.assign',
  requestFulfil: 'request.fulfil',
  requestClose: 'request.close',
  requestCancel: 'request.cancel',
  requestCommentCreate: 'request.comment.create',
  requestWorkNoteRead: 'request.worknote.read',
  requestWorkNoteCreate: 'request.worknote.create',

  problemRead: 'problem.read',
  problemCreate: 'problem.create',
  problemUpdate: 'problem.update',
  problemAssign: 'problem.assign',
  problemInvestigate: 'problem.investigate',
  problemPublishKnownError: 'problem.publish_known_error',
  problemResolve: 'problem.resolve',
  problemClose: 'problem.close',
  problemCancel: 'problem.cancel',
  problemCommentCreate: 'problem.comment.create',
  problemWorkNoteRead: 'problem.worknote.read',
  problemLinkIncident: 'problem.link_incident',

  changeRead: 'change.read',
  changeCreate: 'change.create',
  changeUpdate: 'change.update',
  changeAssign: 'change.assign',
  changeSchedule: 'change.schedule',
  changeImplement: 'change.implement',
  changeReview: 'change.review',
  changeClose: 'change.close',
  changeCancel: 'change.cancel',
  changeCommentCreate: 'change.comment.create',
  changeWorkNoteRead: 'change.worknote.read',
  changeRaiseEmergency: 'change.raise_emergency',

  approvalAct: 'approval.act',
  approvalReadAll: 'approval.read.all',

  categoryRead: 'category.read',
  categoryManage: 'category.manage',
  groupRead: 'group.read',
  groupManage: 'group.manage',
  userRead: 'user.read',
  userManage: 'user.manage',
  roleRead: 'role.read',
  roleManage: 'role.manage',

  slaRead: 'sla.read',
  slaManage: 'sla.manage',
  calendarRead: 'calendar.read',
  calendarManage: 'calendar.manage',

  auditRead: 'audit.read',
  settingRead: 'setting.read',
  settingManage: 'setting.manage',

  reportView: 'report.view',
  reportExport: 'report.export',

  aiAssistantUse: 'ai.assistant.use',
  aiActionConfirm: 'ai.action.confirm',
  aiManage: 'ai.manage',
} as const;

export type PermissionCode = (typeof Permissions)[keyof typeof Permissions];
