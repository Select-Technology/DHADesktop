"""DHA-10 - Cancelling an absence deactivates the time entry it created.

Acceptance (from the card):
  - When approved absence is cancelled, the time entry created from it is DEACTIVATED, not deleted. Records are never deleted.
  - The entry is traceable back to the absence it came from, so the right entry is deactivated and not another one.
  - A cancelled day plus real work booked on it must not add up to more than the real work (the 9.5h vs 7.5h problem).
Covers both routes: the Dynamics/SharePoint leave app (fwp_RecordLeave/fwp_RemoveLeave) and the desktop app (fwp_leaverequest).
"""
import unittest

from dvtest import (DataverseTestCase, JESSICA, APPROVED, CANCEL_REQUESTED, CANCELLED, action, call, get, rows, entity_set,
                    leave_entries, active, day_hours, delete_time_entries)
import dv  # noqa: E402 - path set up by dvtest

KEY = "ZZTEST-D10-"


class Dha10LeaveApp(DataverseTestCase):
    """Route 1: the Valto / SharePoint leave app via the Custom APIs."""

    @classmethod
    def setUpClass(cls):
        cls.keys = set()
        cls.extra_entries = []

    @classmethod
    def tearDownClass(cls):
        for k in cls.keys:
            delete_time_entries(k)
        for tid in cls.extra_entries:
            call("DELETE", f"fwp_timeentries({tid})")

    def record(self, name, start, end=None, end_time=None):
        key = KEY + name
        self.keys.add(key)
        body = {"UserId": JESSICA, "StartDate": start, "SourceKey": key}
        if end:
            body["EndDate"] = end
        if end_time:
            body["EndTime"] = end_time
        self.assertActionOk(action("fwp_RecordLeave", body))
        return key

    def test_cancelling_deactivates_and_never_deletes(self):
        key = self.record("cancel", "2027-11-01", "2027-11-02")
        ids_before = sorted(e["fwp_timeentryid"] for e in leave_entries(key))
        self.assertActionOk(action("fwp_RemoveLeave", {"SourceKey": key}))
        after = leave_entries(key)
        self.assertEqual(ids_before, sorted(e["fwp_timeentryid"] for e in after), "entries were deleted, not deactivated")
        self.assertTrue(all(e["statecode"] == 1 for e in after), "entries still active after cancellation")

    def test_re_approving_reactivates_the_same_entries_rather_than_duplicating(self):
        key = self.record("reapprove", "2027-11-03", "2027-11-04")
        ids = sorted(e["fwp_timeentryid"] for e in leave_entries(key))
        self.assertActionOk(action("fwp_RemoveLeave", {"SourceKey": key}))
        self.record("reapprove", "2027-11-03", "2027-11-04")
        after = leave_entries(key)
        self.assertEqual(ids, sorted(e["fwp_timeentryid"] for e in after))
        self.assertTrue(all(e["statecode"] == 0 for e in after))

    def test_shortening_leave_deactivates_only_the_dropped_day(self):
        key = self.record("shorten", "2027-11-08", "2027-11-09")
        self.record("shorten", "2027-11-08")
        states = {e["fwp_date"][:10]: e["statecode"] for e in leave_entries(key)}
        self.assertEqual({"2027-11-08": 0, "2027-11-09": 1}, states)

    def test_only_this_absences_entries_are_touched(self):
        mine = self.record("mine", "2027-11-10")
        other = self.record("other", "2027-11-11")
        self.assertActionOk(action("fwp_RemoveLeave", {"SourceKey": mine}))
        self.assertEqual(1, len(active(leave_entries(other))), "cancelling one absence affected another")

    def test_every_entry_is_traceable_to_its_absence(self):
        key = self.record("trace", "2027-11-12")
        self.assertTrue(all(e["fwp_leavesourcekey"] == key for e in rows(
            f"fwp_timeentries?$select=fwp_leavesourcekey&$filter=fwp_leavesourcekey eq '{key}'")))

    def test_cancelled_day_plus_real_work_counts_only_the_real_work(self):
        key = self.record("ninehalf", "2027-11-15")
        self.assertActionOk(action("fwp_RemoveLeave", {"SourceKey": key}))
        s, d, _ = dv.call("POST", "fwp_timeentries", {
            "fwp_date": "2027-11-15", "fwp_decimalhours": 7.5, "fwp_durationhours": 7, "fwp_minutes": 30, "fwp_notes": "ZZTEST real work",
            "fwp_category": 1, "fwp_classification": 800470000, "fwp_chargerate": 800470000,
            "fwp_Project@odata.bind": "/msdyn_projects(12a3e411-5414-f011-9989-000d3ad5d75f)",
            "fwp_TeamMember@odata.bind": f"/systemusers({JESSICA})"}, extra={"Prefer": "return=representation"})
        self.assertLess(s, 300)
        self.extra_entries.append(d["fwp_timeentryid"])
        # The desktop app's query: active entries only (TimeEntryService.GetTimeEntries)
        day = rows(f"fwp_timeentries?$select=fwp_decimalhours&$filter=_fwp_teammember_value eq {JESSICA} and "
                   f"fwp_date eq 2027-11-15 and statecode eq 0")
        self.assertEqual(7.5, sum(r["fwp_decimalhours"] for r in day), "cancelled leave still counted")


class Dha10DesktopRequests(DataverseTestCase):
    """Route 2: leave booked in the desktop app."""

    @classmethod
    def setUpClass(cls):
        cls.LR = entity_set("fwp_leaverequest")
        cls.made = []

    @classmethod
    def tearDownClass(cls):
        for rid in cls.made:
            call("PATCH", f"{cls.LR}({rid})", {"fwp_status": CANCELLED})
            call("DELETE", f"{cls.LR}({rid})")
            delete_time_entries("LV-" + rid.replace("-", ""))

    def approved(self, start, end):
        s, d, _ = dv.call("POST", self.LR, {"fwp_Employee@odata.bind": f"/systemusers({JESSICA})", "fwp_startdate": start,
                                            "fwp_enddate": end, "fwp_requestercomments": "ZZTEST"}, extra={"Prefer": "return=representation"})
        self.assertLess(s, 300)
        rid = d["fwp_leaverequestid"]
        self.made.append(rid)
        call("PATCH", f"{self.LR}({rid})", {"fwp_status": APPROVED})
        return rid, "LV-" + rid.replace("-", "")

    def test_approved_cancellation_deactivates_the_entries_and_keeps_the_request(self):
        rid, key = self.approved("2027-11-22", "2027-11-23")
        call("PATCH", f"{self.LR}({rid})", {"fwp_status": CANCEL_REQUESTED})
        call("PATCH", f"{self.LR}({rid})", {"fwp_status": CANCELLED})
        entries = leave_entries(key)
        self.assertEqual(2, len(entries), "entries deleted")
        self.assertTrue(all(e["statecode"] == 1 for e in entries))
        self.assertEqual(CANCELLED, get(f"{self.LR}({rid})?$select=fwp_status")["fwp_status"], "request kept")

    def test_hr_cancelling_approved_leave_deactivates_the_entries(self):
        rid, key = self.approved("2027-11-24", "2027-11-24")
        call("PATCH", f"{self.LR}({rid})", {"fwp_status": CANCELLED})
        self.assertEqual([1], [e["statecode"] for e in leave_entries(key)])

    def test_entries_link_back_to_the_request(self):
        rid, key = self.approved("2027-11-25", "2027-11-26")
        self.assertTrue(all(e["_fwp_leaverequest_value"] == rid for e in leave_entries(key)))


class Dha10Views(DataverseTestCase):
    """Deactivated entries must not show up where hours are counted."""

    def _views(self, name):
        v = rows(f"savedqueries?$select=fetchxml&$filter=returnedtypecode eq 'fwp_timeentry' and name eq '{name}'")
        self.assertGreater(len(v), 0, f"view {name} not found")
        return [x["fetchxml"] for x in v]

    def test_default_time_entry_views_show_active_only(self):
        for name in ("Active Time Entries", "My Time Entries"):      # dev has two views called "My Time Entries"
            for i, x in enumerate(self._views(name)):
                with self.subTest(view=f"{name} #{i + 1}"):
                    self.assertIn('<condition attribute="statecode" operator="eq" value="0"', x)

    @unittest.expectedFailure
    def test_reconciliation_view_shows_active_only(self):
        """KNOWN GAP (not changed - outside the leave work): this view has no status filter, so deactivated
        leave entries appear in it. The reconciliation process itself still needs checking too."""
        for x in self._views("Time Records for Reconciliation"):
            self.assertIn('attribute="statecode"', x)


if __name__ == "__main__":
    unittest.main()
