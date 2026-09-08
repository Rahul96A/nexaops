using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Api.Seeding;

/// <summary>One realistic incident pattern used to populate the demo queue.</summary>
/// <param name="Title">Short summary, as an employee would write it.</param>
/// <param name="Description">The detail an agent would actually receive.</param>
/// <param name="CategoryCode">Category to classify under.</param>
/// <param name="SubcategoryCode">Subcategory to classify under.</param>
/// <param name="GroupCode">Assignment group that owns this kind of work.</param>
/// <param name="Impact">Business impact.</param>
/// <param name="Urgency">How soon it is needed.</param>
/// <param name="Channel">How it arrived.</param>
/// <param name="Tags">Search tags.</param>
/// <param name="AgentAcknowledgement">The first agent reply, which stops the response clock.</param>
/// <param name="InvestigationNote">An internal work note showing diagnosis.</param>
/// <param name="Resolution">Resolution text.</param>
/// <param name="ResolutionCode">Closure classification.</param>
public sealed record IncidentScenario(
    string Title,
    string Description,
    string CategoryCode,
    string SubcategoryCode,
    string GroupCode,
    Impact Impact,
    Urgency Urgency,
    IncidentChannel Channel,
    string[] Tags,
    string AgentAcknowledgement,
    string InvestigationNote,
    string Resolution,
    ResolutionCode ResolutionCode);

/// <summary>
/// The library of incident patterns behind the demo data.
/// <para>
/// These are written the way an Indian enterprise service desk actually sees them - VPN and
/// connectivity problems, ERP and payroll issues, laptop failures, phishing reports - so that a
/// demo shows a queue a prospect recognises rather than obvious filler. Every person, company
/// and system named is fictional.
/// </para>
/// </summary>
public static class IncidentCatalogue
{
    public static List<IncidentScenario> Build() =>
    [
        new("Cannot connect to the corporate VPN since this morning",
            "The VPN client reports 'authentication failed' every time I try to connect. It was working when I " +
            "logged off last night. I have restarted the laptop and tried both office wifi and my home broadband. " +
            "I cannot reach any internal system, so I am completely blocked.",
            "NETWORK", "VPN", "NET-OPS",
            Impact.Significant, Urgency.High, IncidentChannel.Phone,
            ["vpn", "connectivity", "remote-access"],
            "Thanks for reporting this. I can see failed authentication attempts against the VPN gateway from your " +
            "account. I am checking whether this is specific to you or wider.",
            "Gateway logs show certificate validation failures for clients on the 4.9.x agent build. The certificate " +
            "chain was rotated overnight and the older agent does not trust the new intermediate.",
            "The VPN client was upgraded to 4.10.2, which trusts the new certificate chain. Connectivity confirmed " +
            "with the user on both office and home networks.",
            ResolutionCode.Resolved),

        new("VPN drops every few minutes on home broadband",
            "The VPN connects but disconnects roughly every five to ten minutes. I have to reconnect constantly, " +
            "which is making calls and file transfers impossible.",
            "NETWORK", "VPN", "NET-OPS",
            Impact.Moderate, Urgency.High, IncidentChannel.Portal,
            ["vpn", "instability"],
            "I have your session logs. I can see repeated re-key failures. Let us try switching your client to TCP " +
            "transport while I look into the gateway side.",
            "Re-key failures correlate with MTU fragmentation on the user's ISP link. UDP transport is being dropped " +
            "mid-session.",
            "Client transport switched to TCP with an MTU of 1350. The session has now been stable for over four hours.",
            ResolutionCode.Resolved),

        new("Office wifi keeps disconnecting on the third floor",
            "Several of us on the third floor near the east meeting rooms are losing wifi repeatedly. It reconnects " +
            "on its own after a minute but it is disrupting video calls.",
            "NETWORK", "WIFI", "NET-OPS",
            Impact.Significant, Urgency.Medium, IncidentChannel.WalkIn,
            ["wifi", "office-network"],
            "Thanks for flagging this. I can see the access point in that area reporting high channel utilisation. " +
            "I am investigating.",
            "AP-3E-04 is showing channel interference from a neighbouring tenant on the same channel, plus client " +
            "count above the configured maximum during meeting hours.",
            "The access point was moved to a clear channel and a second AP was enabled in the east meeting area to " +
            "spread the client load. No disconnections reported since.",
            ResolutionCode.Resolved),

        new("No internet access from the Pune office",
            "Nobody in the Pune office can reach external websites. Internal applications work normally. This " +
            "started about twenty minutes ago and the whole floor is affected.",
            "NETWORK", "INTERNET", "NET-OPS",
            Impact.Extensive, Urgency.Critical, IncidentChannel.Phone,
            ["outage", "internet", "pune", "major"],
            "We have confirmed the outage and raised it as a critical incident. Our ISP has been engaged and we are " +
            "on a bridge call with them now.",
            "Primary ISP link is down at the provider end. Automatic failover to the secondary link did not trigger " +
            "because the BGP session was administratively held down after last month's maintenance.",
            "The secondary link was brought up manually and traffic restored. The ISP repaired the primary circuit " +
            "the same evening. A problem record has been raised to fix the failover configuration.",
            ResolutionCode.Resolved),

        new("Laptop will not power on",
            "My laptop was working yesterday. This morning it does not respond at all when I press the power " +
            "button, with or without the charger connected. There is no light on the charger either.",
            "HARDWARE", "LAPTOP", "SD-L1",
            Impact.Moderate, Urgency.High, IncidentChannel.Phone,
            ["hardware", "laptop", "power"],
            "Sorry to hear that. I have arranged a loan laptop so you are not blocked while we diagnose yours. " +
            "Please collect it from the IT desk on the second floor.",
            "Charger tested on a known-good unit and confirmed dead. Laptop powers on with a replacement charger, " +
            "so the machine itself is fine.",
            "The power adapter had failed and was replaced. The laptop was tested and returned to the user, and the " +
            "loan unit has been checked back in.",
            ResolutionCode.Resolved),

        new("Second monitor not detected after docking station change",
            "Since the docking station was swapped, only one of my two monitors is detected. I have tried both " +
            "ports and swapped the cables between the monitors.",
            "HARDWARE", "PERIPHERAL", "SD-L1",
            Impact.Minor, Urgency.Medium, IncidentChannel.Portal,
            ["hardware", "monitor", "docking"],
            "Thanks for the detail, that already rules out a cable fault. I will check the dock firmware level.",
            "The replacement dock is on firmware 1.2.4, which has a known dual-display defect on this laptop model. " +
            "Current release is 1.5.1.",
            "The dock firmware was updated to 1.5.1 and both monitors are now detected at full resolution.",
            ResolutionCode.Resolved),

        new("Unable to print to the finance floor printer",
            "Print jobs sit in the queue and never come out. The printer display shows 'ready'. Others on my floor " +
            "have the same problem.",
            "HARDWARE", "PRINTER", "SD-L1",
            Impact.Moderate, Urgency.Medium, IncidentChannel.Portal,
            ["printer", "finance"],
            "I can see the queue backing up on the print server. Investigating now.",
            "The print spooler on PRT-SRV-02 is stuck with a corrupt job at the head of the queue submitted from a " +
            "legacy driver.",
            "The corrupt job was cleared, the spooler restarted, and the legacy driver replaced with the universal " +
            "print driver on the affected workstations.",
            ResolutionCode.Resolved),

        new("Password reset required - account locked out",
            "I have been locked out of my account after mistyping my password. I have an external client meeting " +
            "in half an hour and cannot access my presentation.",
            "ACCESS", "ACCOUNT", "SD-L1",
            Impact.Minor, Urgency.High, IncidentChannel.Phone,
            ["password", "lockout"],
            "I have verified your identity against your employee record. Unlocking your account now.",
            "Lockout traced to a stale credential cached on the user's mobile mail client, which was retrying with " +
            "the old password.",
            "Account unlocked and password reset. The mobile mail profile was re-authenticated to stop the repeated " +
            "failed attempts.",
            ResolutionCode.Resolved),

        new("Multi-factor authentication prompts not arriving",
            "I am not receiving the authentication prompt on my phone. I have reinstalled the authenticator app but " +
            "still nothing comes through.",
            "ACCESS", "MFA", "SD-L1",
            Impact.Moderate, Urgency.High, IncidentChannel.Chat,
            ["mfa", "authentication"],
            "Reinstalling the app removes the registered device, which is why prompts stopped. I will send you a " +
            "temporary access pass so you can re-register.",
            "User's device registration was removed when the app was reinstalled. No sign-in risk detections on the " +
            "account.",
            "A temporary access pass was issued and the user re-registered their device. Sign-in confirmed working.",
            ResolutionCode.UserEducated),

        new("Cannot access the shared finance drive",
            "I get 'access denied' when opening the finance shared drive. I could open it last week. I need the " +
            "quarter-end templates today.",
            "ACCESS", "PERMISSION", "SD-L1",
            Impact.Moderate, Urgency.High, IncidentChannel.Portal,
            ["permissions", "file-share", "finance"],
            "I can see your group membership changed during the recent departmental restructure. Checking with the " +
            "data owner now.",
            "The user was moved out of FIN-READERS when their department code was updated. The change was correct " +
            "for the restructure but the replacement group was not applied.",
            "Membership of FIN-QUARTER-END was granted after approval from the finance data owner. Access confirmed " +
            "by the user.",
            ResolutionCode.Resolved),

        new("Outlook keeps asking for credentials",
            "Outlook prompts for my password every few minutes and then rejects it. Webmail works normally with the " +
            "same password.",
            "EMAIL", "MAILBOX", "APP-SUP",
            Impact.Moderate, Urgency.Medium, IncidentChannel.Portal,
            ["email", "outlook", "authentication"],
            "Since webmail works, this is almost certainly a cached credential problem on the desktop client rather " +
            "than your account. Let me take a look.",
            "Stale modern authentication token in the local credential store, left behind after the recent tenant " +
            "authentication policy change.",
            "The cached credentials were cleared and the Outlook profile re-created. The client has authenticated " +
            "cleanly since.",
            ResolutionCode.Resolved),

        new("Meeting invitations not reaching external attendees",
            "Clients are telling me they never received my meeting invitations. Internal colleagues receive them " +
            "without any problem.",
            "EMAIL", "CALENDAR", "APP-SUP",
            Impact.Moderate, Urgency.Medium, IncidentChannel.Email,
            ["email", "calendar", "external"],
            "I have pulled the message trace for the invitations you sent yesterday. Investigating where they are " +
            "being stopped.",
            "Message trace shows the invitations rejected by recipient domains for SPF alignment failures on the " +
            "calendar service sending address.",
            "The SPF record was updated to include the calendar service sending host. Test invitations to external " +
            "recipients now deliver.",
            ResolutionCode.Resolved),

        new("ERP posting screen very slow at month end",
            "The journal posting screen takes over a minute per entry. We have hundreds of entries to post before " +
            "the month-end cutoff tonight.",
            "SOFTWARE", "ERP", "APP-SUP",
            Impact.Extensive, Urgency.Critical, IncidentChannel.Phone,
            ["erp", "performance", "month-end", "finance"],
            "Understood, this is blocking the month-end close. I have escalated to the application team and we are " +
            "looking at the database now.",
            "Query plan regression on the journal posting stored procedure after last week's statistics update. " +
            "Reads have increased roughly fortyfold.",
            "Statistics were rebuilt and the plan cache for the affected procedure cleared. Posting time is back " +
            "under two seconds per entry. A change has been raised to add the missing index permanently.",
            ResolutionCode.Resolved),

        new("CRM opportunity records not saving",
            "When I click save on an opportunity, the spinner runs and then it says 'unable to save'. Nothing is " +
            "written. I have lost two updates already.",
            "SOFTWARE", "CRM", "APP-SUP",
            Impact.Significant, Urgency.High, IncidentChannel.Portal,
            ["crm", "sales"],
            "Thanks, I can reproduce this on a test opportunity. Escalating to the application team now.",
            "Validation rule deployed yesterday requires a field that is not present on the sales user layout, so " +
            "the save fails server-side with no visible field error.",
            "The layout was corrected to include the mandatory field and the validation error is now shown inline. " +
            "Both lost updates were recovered from the audit log and re-entered.",
            ResolutionCode.ResolvedByChange),

        new("Excel crashes when opening the budget workbook",
            "Excel closes without warning whenever I open the consolidated budget workbook. Other files open fine " +
            "and colleagues can open the same file.",
            "SOFTWARE", "OFFICE", "APP-SUP",
            Impact.Moderate, Urgency.Medium, IncidentChannel.Portal,
            ["excel", "crash", "finance"],
            "Since colleagues can open the same workbook, this looks local to your installation. I will check your " +
            "add-ins.",
            "Crash dump points to a third-party reporting add-in that is incompatible with the current Excel build.",
            "The incompatible add-in was removed and replaced with the supported version. The workbook now opens " +
            "normally.",
            ResolutionCode.Resolved),

        new("Suspicious email asking for payroll bank details",
            "I received an email that looks like it is from the CFO asking me to update bank details for a payroll " +
            "run. The tone feels wrong and the reply address is not our domain. I have not clicked anything.",
            "SECURITY", "PHISHING", "SEC-OPS",
            Impact.Significant, Urgency.Critical, IncidentChannel.Email,
            ["phishing", "security", "payroll"],
            "Thank you for reporting this rather than acting on it - that was exactly the right call. The security " +
            "team is investigating and we are checking whether anyone else received it.",
            "Display-name spoofing of the CFO from an external domain registered four days ago. Nineteen recipients " +
            "in finance and HR. No clicks recorded in the proxy logs.",
            "The sender domain was blocked at the mail gateway, all copies were purged from recipient mailboxes, and " +
            "a targeted awareness note went to finance and HR. No credentials were entered.",
            ResolutionCode.Resolved),

        new("Endpoint protection flagged a file on my laptop",
            "A warning popped up saying a file was quarantined. I was installing a utility I downloaded to convert " +
            "a PDF. Should I be worried?",
            "SECURITY", "MALWARE", "SEC-OPS",
            Impact.Moderate, Urgency.High, IncidentChannel.Portal,
            ["malware", "endpoint", "security"],
            "The file was quarantined before it could run, so you are not at risk. I am reviewing the detection and " +
            "will confirm the device is clean.",
            "Detection is a bundled adware installer from an unofficial download site. Quarantine succeeded, no " +
            "execution, no network callbacks observed.",
            "The quarantined file was removed and a full scan returned clean. The user was pointed to the approved " +
            "software catalogue for PDF tools.",
            ResolutionCode.UserEducated),

        new("Production database running out of storage",
            "Monitoring has raised an alert that the primary database is above ninety percent storage utilisation " +
            "and climbing.",
            "CLOUD", "DATABASE", "INFRA",
            Impact.Extensive, Urgency.Critical, IncidentChannel.Monitoring,
            ["database", "storage", "capacity", "major"],
            "Acknowledged. We are adding capacity immediately and investigating what is consuming the growth.",
            "Transaction log growth caused by a long-running replication task that has not checkpointed since " +
            "Sunday. Data file growth is normal.",
            "Storage was expanded, the stalled replication task restarted, and the log truncated after a successful " +
            "checkpoint. Utilisation is back to sixty-two percent. A problem record has been raised.",
            ResolutionCode.Resolved),

        new("Nightly backup job failed for the reporting server",
            "The backup job for the reporting server reported failure overnight. This is the second failure this " +
            "week.",
            "CLOUD", "STORAGE", "INFRA",
            Impact.Significant, Urgency.High, IncidentChannel.Monitoring,
            ["backup", "reporting"],
            "Picked up. Reviewing the job history and the target storage account now.",
            "Backup writes are failing with throttling errors against the storage account, which is shared with the " +
            "analytics export job that now runs in the same window.",
            "The backup window was moved to avoid the analytics export and the storage account tier raised. Two " +
            "consecutive successful backups have completed and been verified.",
            ResolutionCode.Resolved),

        new("Virtual machine unresponsive after patching",
            "One of the application servers has not come back after the patching window. It responds to ping but " +
            "the application port is closed.",
            "CLOUD", "SERVER", "INFRA",
            Impact.Significant, Urgency.Critical, IncidentChannel.Monitoring,
            ["vm", "patching", "azure"],
            "Confirmed, the application service is not starting. Taking a look at the boot diagnostics now.",
            "The application service is failing to start because a dependent service is set to manual and did not " +
            "start after the reboot.",
            "The dependent service start-up type was corrected to automatic and the application service started. " +
            "The configuration drift has been fixed in the deployment template.",
            ResolutionCode.ResolvedByChange),

        new("New starter has no laptop on their first day",
            "Our new analyst started this morning and no laptop was allocated. They cannot do anything until this " +
            "is sorted.",
            "HARDWARE", "LAPTOP", "SD-L1",
            Impact.Moderate, Urgency.Critical, IncidentChannel.WalkIn,
            ["onboarding", "asset", "hr"],
            "Apologies for this. I have allocated a device from stock and it is being built now. It will be ready " +
            "within two hours.",
            "The onboarding request was raised after the asset cut-off, so no device was reserved. Stock is " +
            "available.",
            "A laptop was allocated from stock, built to the standard image, and handed over. The asset record has " +
            "been assigned to the new starter.",
            ResolutionCode.Resolved),

        new("Mobile device cannot sync corporate email",
            "My phone stopped syncing work email after the security update. Personal accounts on the same phone " +
            "work normally.",
            "HARDWARE", "MOBILE", "SD-L1",
            Impact.Minor, Urgency.Medium, IncidentChannel.Portal,
            ["mobile", "email"],
            "The update will have reset the device compliance state. I will check what the management console says.",
            "Device is reporting as non-compliant because the OS version is below the minimum in the compliance " +
            "policy, so conditional access is blocking mail.",
            "The user updated the device to the required OS version and compliance was re-evaluated. Mail is syncing " +
            "again.",
            ResolutionCode.UserEducated),

        new("Browser blocks the vendor portal as insecure",
            "The vendor portal we use for purchase orders now shows a certificate warning and the browser refuses " +
            "to load it.",
            "SOFTWARE", "BROWSER", "APP-SUP",
            Impact.Moderate, Urgency.Medium, IncidentChannel.Portal,
            ["browser", "certificate", "vendor"],
            "Thanks. I can reproduce it. The vendor's certificate appears to have expired, so this may be on their " +
            "side. Confirming now.",
            "The vendor's TLS certificate expired at midnight. Confirmed against a public certificate checker; " +
            "nothing to change on our side.",
            "The vendor renewed their certificate and the portal loads correctly. No change was required to our " +
            "systems.",
            ResolutionCode.NoFaultFound),

        new("Windows update loop on my desktop",
            "My desktop keeps restarting and saying it is configuring updates, then rolls back and starts again. " +
            "I have not been able to log in all morning.",
            "SOFTWARE", "OS", "SD-L1",
            Impact.Moderate, Urgency.High, IncidentChannel.Phone,
            ["windows", "updates"],
            "That loop is usually a single failed update rather than the machine itself. I will connect and get the " +
            "logs.",
            "One cumulative update is failing repeatedly with a component store corruption error.",
            "The component store was repaired and the failed update reinstalled successfully. The machine has booted " +
            "normally three times since.",
            ResolutionCode.Resolved),

        new("Collaboration workspace missing after department change",
            "My project workspace has vanished from the app. Colleagues can still see it and say I am no longer " +
            "listed as a member.",
            "EMAIL", "TEAMS", "APP-SUP",
            Impact.Minor, Urgency.Medium, IncidentChannel.Chat,
            ["collaboration", "access"],
            "Your membership was removed by an automated group rule when your department changed. I am arranging to " +
            "have it restored.",
            "Dynamic group rule keyed on department removed the user when their department attribute was updated " +
            "during the restructure.",
            "Membership was restored by the workspace owner and the user was added to the static exception list so " +
            "the rule does not remove them again.",
            ResolutionCode.Resolved),

        new("Firewall rule blocking access to the partner API",
            "Our integration with the logistics partner is failing with connection timeouts. It worked until the " +
            "network change last night.",
            "NETWORK", "FIREWALL", "NET-OPS",
            Impact.Significant, Urgency.High, IncidentChannel.Api,
            ["firewall", "integration", "api"],
            "We made firewall changes overnight, so that is the obvious place to start. Reviewing the rule set now.",
            "The partner's API endpoint moved to a new address range last week. Last night's rule tidy-up removed " +
            "the older permissive rule that was still covering it.",
            "A specific outbound rule was added for the partner's current address range. The integration reconnected " +
            "and the backlog has cleared.",
            ResolutionCode.Resolved),

        new("Shared mailbox not receiving external messages",
            "The support shared mailbox is receiving internal mail but nothing from customers. We think we have " +
            "missed messages since yesterday.",
            "EMAIL", "MAILBOX", "APP-SUP",
            Impact.Significant, Urgency.High, IncidentChannel.Portal,
            ["email", "shared-mailbox"],
            "Checking the transport rules and the mailbox configuration now. I will also confirm whether anything " +
            "is sitting in quarantine.",
            "A transport rule added yesterday to reduce spam is matching too broadly and quarantining external mail " +
            "to this mailbox.",
            "The transport rule was narrowed and forty-one quarantined messages were released to the mailbox. No " +
            "customer email was lost.",
            ResolutionCode.Resolved),

        new("Payroll portal unavailable to all HR staff",
            "Nobody in HR can open the payroll portal. It shows a server error page. Payroll is due to be approved " +
            "tomorrow.",
            "SOFTWARE", "ERP", "APP-SUP",
            Impact.Extensive, Urgency.Critical, IncidentChannel.Phone,
            ["payroll", "outage", "hr", "major"],
            "This is being treated as a critical incident. The application team is engaged and we will update every " +
            "thirty minutes.",
            "The application pool is crashing on start-up after a configuration change deployed last night. The " +
            "connection string references a database alias that no longer resolves.",
            "The configuration was rolled back to the previous release and the portal restored. The change has been " +
            "returned to the implementer for correction, and payroll approval was completed on time.",
            ResolutionCode.Resolved),

        new("Slow file access from the Chennai office",
            "Opening documents from the shared drive takes minutes from Chennai. The same files open instantly for " +
            "Bengaluru colleagues.",
            "NETWORK", "LAN", "NET-OPS",
            Impact.Moderate, Urgency.Medium, IncidentChannel.Portal,
            ["performance", "chennai", "file-share"],
            "Thanks for the comparison with Bengaluru, that is useful. I will look at the link utilisation between " +
            "the sites.",
            "The Chennai site link is saturated during business hours by an unthrottled backup replication job that " +
            "was meant to run overnight.",
            "The replication schedule was corrected to run overnight and traffic shaping was applied to the site " +
            "link. File access times are back to normal.",
            ResolutionCode.Resolved),

        new("Request to restore a deleted project folder",
            "A project folder was deleted from the shared drive by mistake. We need it back, it has about three " +
            "weeks of work in it.",
            "CLOUD", "STORAGE", "INFRA",
            Impact.Moderate, Urgency.High, IncidentChannel.Portal,
            ["restore", "backup", "file-share"],
            "We have nightly backups going back ninety days, so this should be recoverable. Confirming the exact " +
            "path and the last known good date with you now.",
            "Folder present in the backup taken two nights before deletion. Restore staged to a temporary location " +
            "for verification before being put back.",
            "The folder was restored from the backup and verified by the project lead before being returned to its " +
            "original location. Nothing was lost.",
            ResolutionCode.Resolved)
    ];
}
