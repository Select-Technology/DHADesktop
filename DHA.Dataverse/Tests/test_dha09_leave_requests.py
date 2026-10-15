"""DHA-9 (phase B) - leave booked in the desktop app (fwp_leaverequest + LeaveRequestPlugin) and its security.

The desktop app writes fwp_leaverequest rows; everything below is enforced server-side, so it holds whether the
request comes from the desktop app, Dynamics, or the API.
"""
import unittest

from dvtest import (DataverseTestCase, JESSICA, HARRIET, FMT, PENDING, APPROVED, REJECTED, CANCEL_REQUESTED, CANCELLED,
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

    @unittest.expectedFailure
    def test_request_booked_on_behalf_is_owned_by_the_employee(self):
        """KNOWN GAP: requests are owned by whoever creates them. When HR books for someone, the employee
        (who only has "own" access through DHA-1) can't see it. The plug-in needs to assign it to the employee."""
        rid = self.created("2027-08-02")
        self.assertEqual(JESSICA, self.request(rid)["_ownerid_value"])


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
        rid = self.created("2027-08-17")
        self.set_status(rid, CANCELLED)
        self.assertEqual("Withdrawn", self.request(rid)["fwp_lastevent"])
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


if __name__ == "__main__":
    unittest.main()
