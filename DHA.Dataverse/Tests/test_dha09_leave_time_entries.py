"""DHA-9 (phase A) - Approved absence creates the matching time entry automatically.

Card requirements tested against the Custom APIs the SharePoint (Valto) flow calls:
  - full days, half days and part days; part days use the booked times (11:00-13:00 = 2 hours)
  - a generic "Approved leave" label - never the absence type; non-chargeable
  - bank holidays included (everyone gets one), and leave is never double-booked onto them
  - one entry per working day, against the right person and Job
"""
import unittest

from dvtest import (DataverseTestCase, JESSICA, HARRIET, ALICE, FMT, action, call, get, rows, leave_entries, active,
                    day_hours, delete_time_entries)
import dv  # noqa: E402 - path set up by dvtest

KEY = "ZZTEST-A-"


def env_value(schema):
    d = rows(f"environmentvariabledefinitions?$select=defaultvalue&$filter=schemaname eq '{schema}'"
             f"&$expand=environmentvariabledefinition_environmentvariablevalue($select=value)")[0]
    vals = d["environmentvariabledefinition_environmentvariablevalue"]
    return vals[0]["value"] if vals else d["defaultvalue"]


class Dha09PhaseA(DataverseTestCase):

    @classmethod
    def setUpClass(cls):
        cls.keys = set()
        cls.leave_job = env_value("fwp_LeaveProjectId")
        cls.bh_job = env_value("fwp_BankHolidayProjectId")
        cls.jessica_email = get(f"systemusers({JESSICA})?$select=internalemailaddress")["internalemailaddress"]

    @classmethod
    def tearDownClass(cls):
        for k in cls.keys:
            delete_time_entries(k)

    def record(self, name, user=JESSICA, start=None, end=None, start_time=None, end_time=None, caller=None, by_email=False):
        key = KEY + name
        self.keys.add(key)
        body = {"StartDate": start, "SourceKey": key}
        if by_email:
            body["UserEmail"] = self.jessica_email
        else:
            body["UserId"] = user
        if end:
            body["EndDate"] = end
        if start_time:
            body["StartTime"] = start_time
        if end_time:
            body["EndTime"] = end_time
        return key, action("fwp_RecordLeave", body, user=caller)

    # ---- hours ----

    def test_full_week_gives_five_working_days_8_5_mon_to_thu_and_8_friday(self):
        key, r = self.record("week", start="2027-03-01", end="2027-03-07")       # Mon-Sun
        out = self.assertActionOk(r)
        self.assertEqual(5, out["EntriesCreated"])
        self.assertEqual(42.0, out["TotalHours"])
        self.assertEqual({"2027-03-01": 8.5, "2027-03-02": 8.5, "2027-03-03": 8.5, "2027-03-04": 8.5, "2027-03-05": 8.0},
                         day_hours(leave_entries(key)), "HR decision 15 Oct 2026: 8.5h Mon-Thu, 8h Fri")

    def test_part_day_11_to_13_is_two_hours(self):
        key, r = self.record("part", start="2027-03-10", start_time="11:00", end_time="13:00")
        self.assertActionOk(r)
        self.assertEqual({"2027-03-10": 2.0}, day_hours(leave_entries(key)))

    def test_half_days_at_each_end_of_a_multi_day_booking(self):
        # Wed from 13:30, Thu all day, Fri until 12:00
        key, r = self.record("multi", start="2027-03-10", end="2027-03-12", start_time="13:30", end_time="12:00")
        self.assertActionOk(r)
        self.assertEqual({"2027-03-10": 4.0, "2027-03-11": 8.5, "2027-03-12": 3.0}, day_hours(leave_entries(key)))

    def test_whole_working_day_window_counts_expected_hours_not_elapsed(self):
        key, r = self.record("window", start="2027-03-15", start_time="09:00", end_time="17:30")
        self.assertActionOk(r)
        self.assertEqual({"2027-03-15": 8.5}, day_hours(leave_entries(key)))

    def test_bank_holidays_in_a_booking_are_skipped(self):
        # Thu 25 Mar - Tue 30 Mar 2027: Good Friday 26th and Easter Monday 29th are bank holidays
        key, r = self.record("easter", start="2027-03-25", end="2027-03-30")
        self.assertActionOk(r)
        self.assertEqual({"2027-03-25": 8.5, "2027-03-30": 8.5}, day_hours(leave_entries(key)))

    def test_part_time_pattern_only_books_working_days(self):
        # Harriet (dev): Mon and Fri off
        key, r = self.record("parttime", user=HARRIET, start="2027-03-01", end="2027-03-05")
        self.assertActionOk(r)
        self.assertEqual({"2027-03-02": 8.5, "2027-03-03": 8.5, "2027-03-04": 8.5}, day_hours(leave_entries(key)))

    def test_colleague_with_times_but_no_hours_falls_back_to_a_standard_day(self):
        # Alice (dev): no expected hours, finish recorded as "5.30" -> 17:30
        key, r = self.record("alice", user=ALICE, start="2027-03-16", start_time="15:00")
        self.assertActionOk(r)
        self.assertEqual({"2027-03-16": 2.5}, day_hours(leave_entries(key)))

    def test_weekend_only_booking_creates_nothing(self):
        key, r = self.record("weekend", start="2027-03-06", end="2027-03-07")
        out = self.assertActionOk(r)
        self.assertEqual(0, out["EntriesCreated"])
        self.assertEqual({}, day_hours(leave_entries(key)))

    def test_end_date_defaults_to_start_date(self):
        key, r = self.record("oneday", start="2027-03-17")
        self.assertActionOk(r)
        self.assertEqual({"2027-03-17": 8.5}, day_hours(leave_entries(key)))

    # ---- Valto half days (StartHalfDay / EndHalfDay) ----

    def test_valto_afternoon_half_day_is_exactly_half_a_day(self):
        key = KEY + "pm-half"
        self.keys.add(key)
        self.assertActionOk(action("fwp_RecordLeave", {"UserId": JESSICA, "StartDate": "2027-03-22", "StartHalfDay": True, "SourceKey": key}))
        self.assertEqual({"2027-03-22": 4.25}, day_hours(leave_entries(key)))

    def test_valto_morning_half_day_is_exactly_half_a_day(self):
        key = KEY + "am-half"
        self.keys.add(key)
        self.assertActionOk(action("fwp_RecordLeave", {"UserId": JESSICA, "StartDate": "2027-03-23", "EndHalfDay": True, "SourceKey": key}))
        self.assertEqual({"2027-03-23": 4.25}, day_hours(leave_entries(key)))

    def test_valto_half_days_at_both_ends_of_a_booking(self):
        key = KEY + "both-halves"
        self.keys.add(key)
        self.assertActionOk(action("fwp_RecordLeave", {"UserId": JESSICA, "StartDate": "2027-04-14", "EndDate": "2027-04-16",
                                                       "StartHalfDay": True, "EndHalfDay": True, "SourceKey": key}))
        self.assertEqual({"2027-04-14": 4.25, "2027-04-15": 8.5, "2027-04-16": 4.0}, day_hours(leave_entries(key)))

    def test_half_day_for_a_part_timer_uses_their_day(self):
        key = KEY + "harriet-half"
        self.keys.add(key)
        self.assertActionOk(action("fwp_RecordLeave", {"UserId": HARRIET, "StartDate": "2027-04-21", "StartHalfDay": True, "SourceKey": key}))
        self.assertEqual({"2027-04-21": 4.25}, day_hours(leave_entries(key)))

    # ---- what the entry looks like ----

    def test_entries_are_generic_non_chargeable_approved_leave_for_the_right_person_and_job(self):
        key, r = self.record("shape", start="2027-03-18")
        self.assertActionOk(r)
        e = leave_entries(key)[0]
        self.assertEqual("Approved leave", e["fwp_notes"], "label must be generic - never the absence type")
        self.assertEqual(2, e["fwp_category"], "Non-chargeable")
        self.assertEqual(800470000, e["fwp_classification"], "Project classification")
        self.assertEqual(self.leave_job, e["_fwp_project_value"])
        self.assertEqual(JESSICA, e["_fwp_teammember_value"])
        self.assertEqual((8, 30), (e["fwp_durationhours"], e["fwp_minutes"]), "hours/minutes split as the desktop app writes it")

    def test_person_can_be_given_by_email(self):
        key, r = self.record("email", start="2027-03-19", by_email=True)
        self.assertActionOk(r)
        self.assertEqual(JESSICA, leave_entries(key)[0]["_fwp_teammember_value"])

    # ---- validation ----

    def test_unknown_email_is_rejected_clearly(self):
        r = action("fwp_RecordLeave", {"UserEmail": "nobody@example.com", "StartDate": "2027-03-19", "SourceKey": KEY + "x"})
        self.assertActionFails(r, "could not find a single active dynamics user")

    def test_source_key_is_required(self):
        r = action("fwp_RecordLeave", {"UserId": JESSICA, "StartDate": "2027-03-19", "SourceKey": ""})
        self.assertActionFails(r, "sourcekey is required")

    def test_badly_formatted_time_is_rejected(self):
        r = action("fwp_RecordLeave", {"UserId": JESSICA, "StartDate": "2027-03-19", "StartTime": "1pm", "SourceKey": KEY + "y"})
        self.assertActionFails(r, "hh:mm")

    def test_end_before_start_is_rejected(self):
        r = action("fwp_RecordLeave", {"UserId": JESSICA, "StartDate": "2027-03-19", "EndDate": "2027-03-18", "SourceKey": KEY + "z"})
        self.assertActionFails(r, "before start")

    # ---- re-sending / removing ----

    def test_resending_the_same_request_changes_nothing(self):
        key, r = self.record("resend", start="2027-04-05", end="2027-04-06")
        self.assertActionOk(r)
        ids = sorted(e["fwp_timeentryid"] for e in leave_entries(key))
        _, r2 = self.record("resend", start="2027-04-05", end="2027-04-06")
        out = self.assertActionOk(r2)
        self.assertEqual((0, 0), (out["EntriesCreated"], out["EntriesRemoved"]))
        self.assertEqual(ids, sorted(e["fwp_timeentryid"] for e in leave_entries(key)), "no duplicates, same rows")

    def test_removing_twice_is_harmless(self):
        key, r = self.record("remove2", start="2027-04-07")
        self.assertActionOk(r)
        self.assertEqual(1, self.assertActionOk(action("fwp_RemoveLeave", {"SourceKey": key}))["EntriesRemoved"])
        self.assertEqual(0, self.assertActionOk(action("fwp_RemoveLeave", {"SourceKey": key}))["EntriesRemoved"])

    def test_removing_an_unknown_key_does_nothing(self):
        self.assertEqual(0, self.assertActionOk(action("fwp_RemoveLeave", {"SourceKey": KEY + "never-used"}))["EntriesRemoved"])

    # ---- reconciled entries ----

    def test_reconciled_entries_are_never_changed(self):
        key, r = self.record("recon", start="2027-04-12", end="2027-04-13")
        self.assertActionOk(r)
        entries = leave_entries(key)
        reconciled = entries[0]["fwp_timeentryid"]
        self.assertLess(call("PATCH", f"fwp_timeentries({reconciled})", {"fwp_calculatedreconciledvalue": 0})[0], 300)

        self.assertEqual(1, self.assertActionOk(action("fwp_RemoveLeave", {"SourceKey": key}))["EntriesRemoved"])
        after = {e["fwp_timeentryid"]: e["statecode"] for e in leave_entries(key)}
        self.assertEqual(0, after[reconciled], "reconciled entry must stay active")

        # Re-approving must not create a second entry on the reconciled day
        self.assertActionOk(self.record("recon", start="2027-04-12", end="2027-04-13")[1])
        days = [e["fwp_date"][:10] for e in active(leave_entries(key))]
        self.assertEqual(["2027-04-12", "2027-04-13"], sorted(days))

    # ---- bank holidays ----

    def test_bank_holiday_entries_for_everyone_on_their_working_days(self):
        # Mon 3 May 2027 (Early May bank holiday). Harriet doesn't work Mondays in dev.
        try:
            out = self.assertActionOk(action("fwp_RecordBankHolidays", {"FromDate": "2027-05-03", "ToDate": "2027-05-03"}))
            self.assertGreater(out["EntriesCreated"], 50, "every active colleague should get one")

            jess = leave_entries(f"BH-2027-05-03-{JESSICA.replace('-', '')}")
            self.assertEqual({"2027-05-03": 8.5}, day_hours(jess))
            self.assertEqual("Bank holiday", jess[0]["fwp_notes"])
            self.assertEqual(self.bh_job, jess[0]["_fwp_project_value"])
            self.assertEqual([], leave_entries(f"BH-2027-05-03-{HARRIET.replace('-', '')}"), "Harriet doesn't work Mondays")

            again = self.assertActionOk(action("fwp_RecordBankHolidays", {"FromDate": "2027-05-03", "ToDate": "2027-05-03"}))
            self.assertEqual(0, again["EntriesCreated"], "daily re-run must not duplicate")
        finally:
            for e in rows("fwp_timeentries?$select=fwp_timeentryid&$filter=startswith(fwp_leavesourcekey,'BH-2027-05-03-')"):
                call("DELETE", f"fwp_timeentries({e['fwp_timeentryid']})")

    def test_range_with_no_bank_holidays_creates_nothing(self):
        out = self.assertActionOk(action("fwp_RecordBankHolidays", {"FromDate": "2027-06-01", "ToDate": "2027-06-30"}))
        self.assertEqual(0, out["EntriesCreated"])

    # ---- who can call it ----

    def test_a_project_manager_cannot_record_leave_for_someone_else(self):
        key = KEY + "pm-for-harriet"
        self.keys.add(key)
        status, body = action("fwp_RecordLeave", {"UserId": HARRIET, "StartDate": "2027-04-20", "SourceKey": key}, user=JESSICA)
        created = active(leave_entries(key))
        self.assertTrue(status >= 400 and not created,
                        f"Jessica (Project Manager) was able to create leave time entries for Harriet: {status} {body}")



class Dha09SharePointFlows(DataverseTestCase):
    """The flows that connect the Valto leave app to the Custom APIs (built off in dev; switched on in prod)."""

    FLOWS = ("Leave Request - Record Time Entries", "Leave - Record Bank Holidays", "Leave Request - Backfill Approved Leave")

    def _flow(self, name):
        import json
        w = rows(f"workflows?$select=clientdata,statecode&$filter=category eq 5 and name eq '{name}'")
        self.assertEqual(1, len(w), f"{name} missing")
        return json.loads(w[0]["clientdata"])["properties"]["definition"]

    def test_flows_exist(self):
        for name in self.FLOWS:
            with self.subTest(flow=name):
                self._flow(name)

    def test_working_from_home_is_excluded_by_default(self):
        self.assertIn("working from home", env_value("fwp_LeaveExcludedRequestTypes").lower())

    def test_request_flow_watches_the_leave_request_list(self):
        d = self._flow("Leave Request - Record Time Entries")
        trig = list(d["triggers"].values())[0]
        self.assertEqual("GetOnUpdatedItems", trig["inputs"]["host"]["operationId"], "must fire on modification (approval/cancel), not just creation")
        self.assertEqual(1, trig["runtimeConfiguration"]["concurrency"]["runs"], "one request at a time")
        self.assertEqual("718145ce-0b2d-4396-af86-ce36aabc8a29", env_value("fwp_LeaveRequestListId"))

    def test_request_flow_records_approved_absence_and_withdraws_everything_else(self):
        import json
        d = json.dumps(self._flow("Leave Request - Record Time Entries"))
        self.assertIn('"actionName": "fwp_RecordLeave"', d)
        self.assertIn('"actionName": "fwp_RemoveLeave"', d)
        self.assertIn("fwp_LeaveExcludedRequestTypes", d)
        self.assertIn("item/StartHalfDay", d)
        self.assertIn("concat('LR-'", d, "source key must be LR-<SharePoint ID> so the right entries are found later")


if __name__ == "__main__":
    unittest.main()

