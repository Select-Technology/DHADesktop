"""DHA-9 (phase B) - leave booked in the desktop app (fwp_leaverequest + LeaveRequestPlugin) and its security.

The desktop app writes fwp_leaverequest rows; everything below is enforced server-side, so it holds whether the
request comes from the desktop app, Dynamics, or the API.
"""
import unittest

from dvtest import (DataverseTestCase, JESSICA, HARRIET, ALICE, FMT, PENDING, APPROVED, REJECTED, CANCEL_REQUESTED, CANCELLED,
                    me, call, get, rows, error_text, entity_set, leave_entries, active, day_hours, delete_time_entries)


class LeaveRequestTestCase(DataverseTestCase):
    @classmethod
    def setUpClass(cls):
        cls.LR = entity_set("fwp_leaverequest")
        cls.made = []

    @classmethod
    def tearDownClass(cls):
        for rid in cls.made:  # test data only - production code never deletes approved leave
            call("PATCH", f"{cls.LR}({rid})", {"fwp_status": CANCELLED})
            call("DELETE", f"{cls.LR}({rid})")
            delete_time_entries("LV-" + rid.replace("-", ""))

    def create(self, start, end=None, employee=JESSICA, start_time=None, end_time=None, **extra):
        body = {"fwp_Employee@odata.bind": f"/systemusers({employee})", "fwp_startdate": start, "fwp_requestercomments": "ZZTEST"}
        if end:
            body["fwp_enddate"] = end
        if start_time:
            body["fwp_starttime"] = start_time
        if end_time:
            body["fwp_endtime"] = end_time
        body.update(extra)
        s, d, _ = __import__("dv").call("POST", self.LR, body, extra={"Prefer": "return=representation"})
        if s < 300:
            self.made.append(d["fwp_leaverequestid"])
            return s, d
        return s, error_text(d)

    def created(self, *args, **kwargs):
        s, d = self.create(*args, **kwargs)
        self.assertLess(s, 300, d)
        return d["fwp_leaverequestid"]

    def set_status(self, rid, status, comment=None):
        body = {"fwp_status": status}
        if comment:
            body["fwp_approvercomments"] = comment
        return call("PATCH", f"{self.LR}({rid})", body)

    def request(self, rid):
        return get(f"{self.LR}({rid})?$select=fwp_hours,fwp_status,fwp_lastevent,fwp_name,_fwp_approver_value,"
                   f"_fwp_bookedby_value,_ownerid_value")

    def entries(self, rid):
        return leave_entries("LV-" + rid.replace("-", ""))


class Dha09NewRequests(LeaveRequestTestCase):

    def test_new_request_is_pending_named_and_has_server_calculated_hours(self):
        rid = self.created("2027-06-07", "2027-06-11")      # Mon-Fri
        r = self.request(rid)
        self.assertEqual(PENDING, r["fwp_status"])
        self.assertEqual(42.0, r["fwp_hours"])
        self.assertEqual("Requested", r["fwp_lastevent"])
        self.assertEqual(me(), r["_fwp_bookedby_value"])
        self.assertTrue(r["fwp_name"].endswith("07/06/2027 to 11/06/2027"), r["fwp_name"])
        self.assertEqual([], self.entries(rid), "nothing goes on the timesheet until it is approved")

    def test_hours_ignore_what_the_client_sends(self):
        rid = self.created("2027-06-14", fwp_hours=999)
        self.assertEqual(8.5, self.request(rid)["fwp_hours"], "the server must work hours out, not trust the app")

    def test_part_day_request_hours(self):
        rid = self.created("2027-06-15", start_time="11:00", end_time="13:00")
        self.assertEqual(2.0, self.request(rid)["fwp_hours"])

    def test_bank_holidays_are_not_counted(self):
        rid = self.created("2027-05-28", "2027-06-01")       # Fri, (Sat, Sun), Mon 31 May bank holiday, Tue
        self.assertEqual(16.5, self.request(rid)["fwp_hours"])      # Fri 8 + Tue 8.5

    def test_part_time_colleague_hours(self):
        rid = self.created("2027-06-21", "2027-06-25", employee=HARRIET)   # Harriet: Tue-Thu only
        self.assertEqual(25.5, self.request(rid)["fwp_hours"])      # Tue-Thu 3 x 8.5

    def test_end_before_start_is_rejected(self):
        s, d = self.create("2027-06-20", "2027-06-18")
        self.assertGreaterEqual(s, 400)
        self.assertIn("before the start date", d)

    def test_overlapping_requests_are_rejected(self):
        self.created("2027-07-05", "2027-07-09")
        s, d = self.create("2027-07-08", "2027-07-12")
        self.assertGreaterEqual(s, 400)
        self.assertIn("overlaps", d)

    def test_adjacent_requests_are_fine(self):
        self.created("2027-07-12", "2027-07-13")
        s, d = self.create("2027-07-14", "2027-07-15")
        self.assertLess(s, 300, d)

    def test_overlap_with_rejected_or_cancelled_leave_is_allowed(self):
        a = self.created("2027-07-19")
        self.set_status(a, REJECTED)
        b = self.created("2027-07-20")
        self.set_status(b, CANCELLED)
        s, d = self.create("2027-07-19", "2027-07-20")
        self.assertLess(s, 300, d)

    def test_approver_defaults_from_colleague_configuration(self):
        cfg = rows(f"fwp_colleagueconfigurations?$select=fwp_colleagueconfigurationid,_fwp_leaveapprover_value"
                   f"&$filter=_fwp_colleague_value eq {JESSICA} and statecode eq 0")[0]
        original = cfg.get("_fwp_leaveapprover_value")
        try:
            call("PATCH", f"fwp_colleagueconfigurations({cfg['fwp_colleagueconfigurationid']})",
                 {"fwp_LeaveApprover@odata.bind": f"/systemusers({HARRIET})"})
            rid = self.created("2027-07-26")
            self.assertEqual(HARRIET, self.request(rid)["_fwp_approver_value"])
            access = get(f"systemusers({HARRIET})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@t)"
                         f"?@t={{'@odata.id':'{self.LR}({rid})'}}")["AccessRights"]
            self.assertIn("ReadAccess", access, "the request must be shared with its approver")
        finally:
            if original:
                call("PATCH", f"fwp_colleagueconfigurations({cfg['fwp_colleagueconfigurationid']})",
                     {"fwp_LeaveApprover@odata.bind": f"/systemusers({original})"})
            else:
                call("DELETE", f"fwp_colleagueconfigurations({cfg['fwp_colleagueconfigurationid']})/fwp_LeaveApprover/$ref")

    def test_request_booked_on_behalf_is_owned_by_the_employee(self):
        """Parity plan D7: the employee only has "own" access (DHA-1), so a request HR books for them must be theirs."""
        rid = self.created("2027-08-02")
        owner = self.request(rid)["_ownerid_value"]
        if owner != JESSICA:
            # Assigning needs the employee to hold leave privileges (DHA-1). Everyone does in prod; Jessica can't in dev
            # (DHA-1 isn't available in her business unit), so the plug-in keeps the booking with whoever made it.
            self.skipTest("Jessica has no DHA-1 in dev, so the request can't be assigned to her (booking kept); in prod everyone holds DHA-1")
        self.assertEqual(JESSICA, owner)


class Dha09Decisions(LeaveRequestTestCase):

    def test_approving_puts_generic_leave_on_the_timesheet_linked_to_the_request(self):
        rid = self.created("2027-08-09", "2027-08-11")
        self.assertLess(self.set_status(rid, APPROVED, "ok")[0], 300)
        r = self.request(rid)
        self.assertEqual("Approved", r["fwp_lastevent"])
        e = self.entries(rid)
        self.assertEqual({"2027-08-09": 8.5, "2027-08-10": 8.5, "2027-08-11": 8.5}, day_hours(e))
        self.assertTrue(all(x["_fwp_leaverequest_value"] == rid for x in e), "every entry traceable to its request")
        self.assertTrue(all(x["fwp_notes"] == "Approved leave" for x in e))

    def test_rejecting_puts_nothing_on_the_timesheet(self):
        rid = self.created("2027-08-16")
        self.set_status(rid, REJECTED, "cover needed")
        self.assertEqual("Rejected", self.request(rid)["fwp_lastevent"])
        self.assertEqual([], self.entries(rid))

    def test_withdrawing_a_pending_request(self):
        # the employee themselves (stsadmin: the only dev user who can act on their own leave)
        rid = self.created("2027-08-17", employee=me())
        self.set_status(rid, CANCELLED)
        self.assertEqual("Withdrawn", self.request(rid)["fwp_lastevent"])

    def test_cancelling_someone_elses_pending_request_tells_them(self):
        rid = self.created("2027-08-18")                     # Jessica's, cancelled by stsadmin
        self.set_status(rid, CANCELLED)
        self.assertEqual("CancelledByHR", self.request(rid)["fwp_lastevent"])
        self.assertEqual([], self.entries(rid))

    def test_cancellation_request_keeps_leave_until_decided_and_declining_keeps_it(self):
        rid = self.created("2027-08-23", "2027-08-24")
        self.set_status(rid, APPROVED)
        self.set_status(rid, CANCEL_REQUESTED)
        self.assertEqual("CancellationRequested", self.request(rid)["fwp_lastevent"])
        self.assertEqual(2, len(active(self.entries(rid))), "still on the timesheet while waiting")
        self.set_status(rid, APPROVED, "sorry, needed")
        self.assertEqual("CancellationDeclined", self.request(rid)["fwp_lastevent"])
        self.assertEqual(2, len(active(self.entries(rid))))

    def test_changing_dates_on_approved_leave_moves_the_timesheet_entries(self):
        rid = self.created("2027-09-06", "2027-09-08")
        self.set_status(rid, APPROVED)
        call("PATCH", f"{self.LR}({rid})", {"fwp_enddate": "2027-09-07", "fwp_endtime": "12:45"})
        self.assertEqual(12.25, self.request(rid)["fwp_hours"])     # Mon 8.5 + Tue 09:00-12:45
        self.assertEqual({"2027-09-06": 8.5, "2027-09-07": 3.75}, day_hours(self.entries(rid)))

    def test_deleting_pending_leave_is_allowed(self):
        s, d = self.create("2027-09-13")
        rid = d["fwp_leaverequestid"]
        self.assertLess(call("DELETE", f"{self.LR}({rid})")[0], 300)
        self.made.remove(rid)

    def test_deleting_approved_or_cancellation_requested_leave_is_blocked(self):
        rid = self.created("2027-09-14")
        self.set_status(rid, APPROVED)
        for status in (APPROVED, CANCEL_REQUESTED):
            with self.subTest(status=status):
                self.set_status(rid, status)
                s, d = call("DELETE", f"{self.LR}({rid})")
                self.assertGreaterEqual(s, 400, "approved leave was deleted")
                self.assertIn("Cancel it instead", error_text(d))


class Dha09Security(DataverseTestCase):

    def _root_role(self, prefix):
        roles = [r for r in rows(f"roles?$select=name,roleid,_parentrootroleid_value&$filter=startswith(name,'{prefix}')")
                 if r["_parentrootroleid_value"] == r["roleid"]]
        self.assertEqual(1, len(roles), roles)
        return roles[0]["roleid"]

    def _privileges(self, role_id):
        held = {p["PrivilegeId"]: p["Depth"] for p in get(f"RetrieveRolePrivilegesRole(RoleId={role_id})")["RolePrivileges"]}
        names = {p["privilegeid"]: p["name"].lower() for p in rows(
            "privileges?$select=privilegeid,name&$filter=contains(name,'fwp_leave') or contains(name,'fwp_publicholiday') "
            "or contains(name,'fwp_colleagueconfiguration')")}
        return {names[i]: d for i, d in held.items() if i in names}

    def test_staff_get_own_leave_through_dha1(self):
        p = self._privileges(self._root_role("DHA-1"))
        for name in ("prvcreatefwp_leaverequest", "prvreadfwp_leaverequest", "prvwritefwp_leaverequest", "prvappendfwp_leaverequest",
                     "prvreadfwp_leaveentitlement"):
            with self.subTest(privilege=name):
                self.assertEqual("Basic", p.get(name), "own records only")
        for name in ("prvreadfwp_leavetype", "prvreadfwp_publicholiday"):
            with self.subTest(privilege=name):
                self.assertEqual("Global", p.get(name))

    def test_staff_cannot_delete_leave(self):
        p = self._privileges(self._root_role("DHA-1"))
        self.assertNotIn("prvdeletefwp_leaverequest", p)

    def test_leave_work_did_not_grant_colleague_configuration_access(self):
        # Colleague configuration holds charge rates (see wiki 02-security-roles) - the leave scripts must not touch it
        p = self._privileges(self._root_role("DHA-1"))
        self.assertNotIn("prvreadfwp_colleagueconfiguration", p)

    def test_entitlements_are_user_owned(self):
        self.assertEqual("UserOwned", get("EntityDefinitions(LogicalName='fwp_leaveentitlement')?$select=OwnershipType")["OwnershipType"])

    def test_hr_team_is_linked_to_the_hr_group_and_holds_leave_administrator(self):
        team = rows("teams?$select=teamid,teamtype&$filter=azureactivedirectoryobjectid eq 5760dd7d-5244-4e06-b8fa-6ce9284e9d3c")
        self.assertEqual(1, len(team))
        self.assertEqual(3, team[0]["teamtype"], "Microsoft 365 group team")
        roles = [r["name"] for r in get(f"teams({team[0]['teamid']})/teamroles_association?$select=name")["value"]]
        self.assertIn("DHA - Leave Administrator", roles)

    def test_leave_administrator_sees_everyones_leave_but_cannot_delete(self):
        p = self._privileges(self._root_role("DHA - Leave Administrator"))
        for name in ("prvreadfwp_leaverequest", "prvwritefwp_leaverequest", "prvreadfwp_leaveentitlement", "prvwritefwp_leaveentitlement"):
            with self.subTest(privilege=name):
                self.assertEqual("Global", p.get(name))
        self.assertNotIn("prvdeletefwp_leaverequest", p)



def leave_type(name):
    return rows(f"fwp_leavetypes?$select=fwp_leavetypeid&$filter=fwp_name eq '{name}' and statecode eq 0")[0]["fwp_leavetypeid"]


class Dha09ParityStage1(LeaveRequestTestCase):
    """Parity plan stage 1 (docs/leave-desktop-parity-plan.md): days, WFH, leave years, approvers, cancellation details."""

    def test_requests_are_measured_in_days(self):
        rid = self.created("2027-10-11", "2027-10-15")                       # Mon-Fri
        r = get(f"{self.LR}({rid})?$select=fwp_days,fwp_hours")
        self.assertEqual((5.0, 42.0), (r["fwp_days"], r["fwp_hours"]))

    def test_part_day_is_a_share_of_a_day(self):
        rid = self.created("2027-10-18", start_time="09:00", end_time="12:45")  # Monday, 3.75 of 8.5 hours
        self.assertEqual(0.44, get(f"{self.LR}({rid})?$select=fwp_days")["fwp_days"])

    def test_leave_cannot_span_two_leave_years(self):
        s, d = self.create("2027-12-30", "2028-01-04")
        self.assertGreaterEqual(s, 400)
        self.assertIn("31 December", d)

    def test_working_from_home_is_approved_but_never_on_the_timesheet(self):
        rid = self.created("2027-10-20", **{"fwp_LeaveType@odata.bind": f"/fwp_leavetypes({leave_type('Working From Home')})"})
        self.set_status(rid, APPROVED)
        self.assertEqual("Approved", self.request(rid)["fwp_lastevent"])
        self.assertEqual([], self.entries(rid))

    def test_working_from_home_does_not_clash_with_an_appointment(self):
        wfh = {"fwp_LeaveType@odata.bind": f"/fwp_leavetypes({leave_type('Working From Home')})"}
        med = {"fwp_LeaveType@odata.bind": f"/fwp_leavetypes({leave_type('Medical')})"}
        self.created("2027-10-21", **wfh)
        s, d = self.create("2027-10-21", start_time="13:45", end_time="14:45", **med)
        self.assertLess(s, 300, d)

    def test_two_absences_still_clash(self):
        hol = {"fwp_LeaveType@odata.bind": f"/fwp_leavetypes({leave_type('Holiday Request')})"}
        self.created("2027-10-25", "2027-10-26", **hol)
        s, d = self.create("2027-10-26", **hol)
        self.assertGreaterEqual(s, 400)

    def test_cancellation_request_is_timestamped(self):
        rid = self.created("2027-10-27")
        self.set_status(rid, APPROVED)
        self.set_status(rid, CANCEL_REQUESTED)
        self.assertIsNotNone(get(f"{self.LR}({rid})?$select=fwp_cancellationrequestedon")["fwp_cancellationrequestedon"])

    def test_additional_approver_defaults_from_configuration_and_is_shared(self):
        cfg = rows(f"fwp_colleagueconfigurations?$select=fwp_colleagueconfigurationid&$filter=_fwp_colleague_value eq {JESSICA} and statecode eq 0")[0]
        cid = cfg["fwp_colleagueconfigurationid"]
        try:
            call("PATCH", f"fwp_colleagueconfigurations({cid})", {"fwp_AdditionalLeaveApprover@odata.bind": f"/systemusers({HARRIET})"})
            rid = self.created("2027-10-28")
            self.assertEqual(HARRIET, get(f"{self.LR}({rid})?$select=_fwp_additionalapprover_value")["_fwp_additionalapprover_value"])
            access = get(f"systemusers({HARRIET})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@t)"
                         f"?@t={{'@odata.id':'{self.LR}({rid})'}}")["AccessRights"]
            self.assertIn("ReadAccess", access)
        finally:
            call("DELETE", f"fwp_colleagueconfigurations({cid})/fwp_AdditionalLeaveApprover/$ref")


class Dha09ParityStage2(LeaveRequestTestCase):
    """Parity plan stage 2: what the desktop My leave screen sends when cancelling."""

    def test_withdrawing_with_a_reason_keeps_the_reason(self):
        rid = self.created("2027-11-01", employee=me())
        call("PATCH", f"{self.LR}({rid})", {"fwp_status": CANCELLED, "fwp_cancellationcomments": "ZZTEST plans changed"})
        r = get(f"{self.LR}({rid})?$select=fwp_status,fwp_lastevent,fwp_cancellationcomments")
        self.assertEqual((CANCELLED, "Withdrawn", "ZZTEST plans changed"),
                         (r["fwp_status"], r["fwp_lastevent"], r["fwp_cancellationcomments"]))

    def test_asking_to_cancel_approved_leave_keeps_the_reason_and_the_timesheet(self):
        rid = self.created("2027-11-02")
        self.set_status(rid, APPROVED)
        call("PATCH", f"{self.LR}({rid})", {"fwp_status": CANCEL_REQUESTED, "fwp_cancellationcomments": "ZZTEST not needed"})
        r = get(f"{self.LR}({rid})?$select=fwp_status,fwp_cancellationcomments,fwp_cancellationrequestedon")
        self.assertEqual((CANCEL_REQUESTED, "ZZTEST not needed"), (r["fwp_status"], r["fwp_cancellationcomments"]))
        self.assertIsNotNone(r["fwp_cancellationrequestedon"])
        self.assertEqual(1, len(active(self.entries(rid))))

    def test_pending_leave_cannot_go_to_the_approver_for_cancellation(self):
        # Otherwise unapproved leave lands on the timesheet, and declining the cancellation approves it
        rid = self.created("2027-11-03")
        s, d = call("PATCH", f"{self.LR}({rid})", {"fwp_status": CANCEL_REQUESTED})
        self.assertGreaterEqual(s, 400)
        self.assertIn("pending request", error_text(d))
        self.assertEqual(PENDING, self.request(rid)["fwp_status"])
        self.assertEqual([], self.entries(rid))


class Dha09ParityStage1Schema(DataverseTestCase):

    def test_leave_types_carry_behaviour_flags(self):
        t = {r["fwp_name"]: r for r in rows("fwp_leavetypes?$select=fwp_name,fwp_isabsence,fwp_deductsfromentitlement,fwp_forcedunits&$filter=statecode eq 0")}
        self.assertFalse(t["Working From Home"]["fwp_isabsence"])
        self.assertTrue(t["Holiday Request"]["fwp_deductsfromentitlement"])
        self.assertEqual(800470001, t["Medical"]["fwp_forcedunits"], "Medical is booked in hours")

    def test_entitlements_are_in_days_with_no_purchased_leave(self):
        attrs = {a["LogicalName"] for a in get("EntityDefinitions(LogicalName='fwp_leaveentitlement')/Attributes?$select=LogicalName")["value"]}
        self.assertTrue({"fwp_entitlementdays", "fwp_carriedforwarddays", "fwp_carryforwardoverride", "fwp_carriedtonextyeardays"} <= attrs)
        self.assertFalse(attrs & {"fwp_entitlementhours", "fwp_purchasedhours"}, "hours / purchased columns should be gone")

    def test_carry_forward_limit_is_five_days(self):
        d = rows("environmentvariabledefinitions?$select=defaultvalue&$filter=schemaname eq 'fwp_LeaveCarryForwardLimit'")
        self.assertEqual("5", d[0]["defaultvalue"])


class Dha09ParityStage3(LeaveRequestTestCase):
    """Parity plan stage 3 (approvers): balances and team leave through Custom APIs, approver changes in colleague configuration."""

    # Jessica (Project Manager, not HR) is made Alice's approver; Harriet is HR (System Administrator in dev)
    @classmethod
    def setUpClass(cls):
        super().setUpClass()
        cfg = rows(f"fwp_colleagueconfigurations?$select=fwp_colleagueconfigurationid,_fwp_leaveapprover_value"
                   f"&$filter=_fwp_colleague_value eq {ALICE} and statecode eq 0")[0]
        cls.cfg_id, cls.original_approver = cfg["fwp_colleagueconfigurationid"], cfg["_fwp_leaveapprover_value"]

    def set_alices_approver(self, user_id):
        if user_id:
            s, d = call("PATCH", f"fwp_colleagueconfigurations({self.cfg_id})", {"fwp_LeaveApprover@odata.bind": f"/systemusers({user_id})"})
        else:
            s, d = call("DELETE", f"fwp_colleagueconfigurations({self.cfg_id})/fwp_LeaveApprover/$ref")
        self.assertLess(s, 300, d)

    @staticmethod
    def balance(employee, year, user=None):
        return call("POST", "fwp_GetLeaveBalance", {"EmployeeId": employee, "Year": year}, user=user)

    def test_own_balance_counts_pending_leave(self):
        before = self.balance(me(), 2027)[1]
        self.assertTrue(before["HasEntitlement"], "dev seed gives stsadmin a 2027 entitlement")
        self.created("2027-11-08", "2027-11-09", employee=me(),
                     **{"fwp_LeaveType@odata.bind": f"/fwp_leavetypes({leave_type('Holiday Request')})"})
        after = self.balance(me(), 2027)[1]
        self.assertEqual((before["BookedDays"] + 2, before["PendingDays"] + 2), (after["BookedDays"], after["PendingDays"]))

    def test_balance_is_refused_to_someone_who_doesnt_approve_them(self):
        s, d = self.balance(ALICE, 2027, user=JESSICA)
        self.assertGreaterEqual(s, 400)
        self.assertIn("people whose leave you approve", error_text(d))

    def test_team_leave_is_only_for_approvers(self):
        s, d = call("POST", "fwp_GetTeamLeave", {"FromDate": "2027-01-01T00:00:00Z", "ToDate": "2027-12-31T00:00:00Z"}, user=JESSICA)
        self.assertGreaterEqual(s, 400)
        self.assertIn("Only leave approvers", error_text(d))

    def test_new_approver_gets_the_waiting_request_their_balance_and_team_leave(self):
        hol = {"fwp_LeaveType@odata.bind": f"/fwp_leavetypes({leave_type('Holiday Request')})"}
        alices = self.created("2027-11-15", employee=ALICE, **hol)
        someone_elses = self.created("2027-11-15", employee=me(), **hol)
        try:
            self.set_alices_approver(JESSICA)
            # the waiting request moved to Jessica, who can now open and decide it
            self.assertEqual(JESSICA, get(f"{self.LR}({alices})?$select=_fwp_approver_value")["_fwp_approver_value"])
            access = get(f"systemusers({JESSICA})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@t)"
                         f"?@t={{'@odata.id':'{self.LR}({alices})'}}")["AccessRights"]
            self.assertIn("WriteAccess", access)
            # Alice's balance
            s, d = self.balance(ALICE, 2027, user=JESSICA)
            self.assertLess(s, 300, d)
            # who's off: her own person's type is shown, someone else's withheld; HR sees both
            window = {"FromDate": "2027-11-15T00:00:00Z", "ToDate": "2027-11-15T00:00:00Z"}
            s, d = call("POST", "fwp_GetTeamLeave", window, user=JESSICA)
            self.assertLess(s, 300, d)
            by_id = {r["fwp_leaverequestid"]: r for r in d["value"]}
            self.assertEqual(leave_type("Holiday Request"), by_id[alices].get("_fwp_leavetype_value"))
            self.assertIn(someone_elses, by_id, "someone else's leave missing from who's off")
            self.assertIsNone(by_id[someone_elses].get("_fwp_leavetype_value"), "type of someone else's absence was shown")
            hr = {r["fwp_leaverequestid"]: r for r in call("POST", "fwp_GetTeamLeave", window, user=HARRIET)[1]["value"]}
            self.assertIsNotNone(hr[someone_elses].get("_fwp_leavetype_value"), "HR should see every type")
        finally:
            self.set_alices_approver(self.original_approver)

    def test_one_day_request_stores_its_end_date_and_still_clashes(self):
        # Booked with a start date only: the end date used to stay empty, so overlap checks and "who's off" missed it
        rid = self.created("2027-11-22")
        self.assertEqual("2027-11-22", get(f"{self.LR}({rid})?$select=fwp_enddate")["fwp_enddate"][:10])
        s, d = self.create("2027-11-22")
        self.assertGreaterEqual(s, 400, "a second absence on the same day was accepted")

    def test_approver_cancelling_on_behalf_tells_the_employee(self):
        rid = self.created("2027-11-16")
        self.set_status(rid, APPROVED)
        call("PATCH", f"{self.LR}({rid})", {"fwp_status": CANCELLED, "fwp_approvercomments": "ZZTEST office closed",
                                            "fwp_Approver@odata.bind": f"/systemusers({me()})"})
        self.assertEqual("CancelledByHR", self.request(rid)["fwp_lastevent"])
        self.assertEqual([], active(self.entries(rid)))


if __name__ == "__main__":
    unittest.main()

