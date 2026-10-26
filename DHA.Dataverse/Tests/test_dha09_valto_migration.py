"""Leave parity stage 8: the Valto -> Dynamics mapping in migrate_valto_leave.py. Offline - no Dataverse or SharePoint calls."""
import datetime as dt
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Provisioning"))
import migrate_valto_leave as m  # noqa: E402

HOURS = [8.5, 8.5, 8.5, 8.5, 8.0, 0, 0]
MON_FRI = [True] * 5 + [False] * 2


def item(**kw):
    base = {"ID": 1, "Status": {"Value": "Approved"}, "RequestCancelled_x003f_": False, "Units": {"Value": "Days"},
            "RequestDayDuration": {"Value": "Full Day"}, "RequestStartDate": "2026-03-02", "RequestEndDate": "2026-03-06",
            "LeaveDuration": 5.0, "Request_x0020_Type": {"Value": "Holiday Request"}}
    base.update(kw)
    return base


class StatusMapping(unittest.TestCase):

    def test_statuses(self):
        self.assertEqual(m.APPROVED, m.status_of(item()))
        self.assertEqual(m.PENDING, m.status_of(item(Status={"Value": "Pending"})))
        self.assertEqual(m.REJECTED, m.status_of(item(Status={"Value": "Rejected"})))
        self.assertEqual(m.CANCELLED, m.status_of(item(Status={"Value": "Cancelled"})))

    def test_approved_with_cancellation_flag_is_waiting_for_the_approver(self):
        self.assertEqual(m.CANCEL_REQUESTED, m.status_of(item(RequestCancelled_x003f_=True)))

    def test_cancelled_stays_cancelled_whatever_the_flag(self):
        self.assertEqual(m.CANCELLED, m.status_of(item(Status={"Value": "Cancelled"}, RequestCancelled_x003f_=True)))


class Times(unittest.TestCase):

    def test_hours_requests_keep_valtos_times(self):
        r = item(Units={"Value": "Hours"}, RequestDayDuration={"Value": "N/A"}, Request_x0020_Start_x0020_Hour=9.0,
                 RequestStartMinutes=45.0, RequestEndHour=11.0, RequestEndMinutes=0.0)
        self.assertEqual(("09:45", "11:00"), m.times_for(r))

    def test_full_days_have_no_times(self):
        self.assertEqual((None, None), m.times_for(item(), (dt.timedelta(hours=8, minutes=30), dt.timedelta(hours=17, minutes=30)), 8.5))

    def test_half_days_kept_as_history_use_valtos_1230(self):
        self.assertEqual((None, "12:30"), m.times_for(item(RequestDayDuration={"Value": "1/2 Day (AM)"})))
        self.assertEqual(("12:30", None), m.times_for(item(RequestDayDuration={"Value": "1/2 Day (PM)"})))

    def test_live_half_days_are_half_a_leave_day_from_the_persons_start_or_finish(self):
        # as the desktop app books them, so the plug-in counts exactly half (8.5h day -> 4h15)
        day = (dt.timedelta(hours=8, minutes=30), dt.timedelta(hours=17, minutes=30))
        self.assertEqual((None, "12:45"), m.times_for(item(RequestDayDuration={"Value": "1/2 Day (AM)"}), day, 8.5))
        self.assertEqual(("13:15", None), m.times_for(item(RequestDayDuration={"Value": "1/2 Day (PM)"}), day, 8.5))
        self.assertEqual(("13:30", None), m.times_for(item(RequestDayDuration={"Value": "1/2 Day (PM)"}), day, 8.0))


class HistoryHours(unittest.TestCase):

    def test_working_days_at_leave_day_hours_skipping_bank_holidays(self):
        # Mon 2 - Fri 6 March 2026: 4 x 8.5 + 8
        self.assertEqual(42.0, m.history_hours(item(), MON_FRI, HOURS, set()))
        self.assertEqual(33.5, m.history_hours(item(), MON_FRI, HOURS, {dt.date(2026, 3, 3)}))

    def test_part_timer_and_half_day(self):
        tue_thu = [False, True, True, True, False, False, False]
        self.assertEqual(25.5, m.history_hours(item(), tue_thu, HOURS, set()))
        half = item(RequestEndDate="2026-03-06", RequestStartDate="2026-03-06", RequestDayDuration={"Value": "1/2 Day (PM)"})
        self.assertEqual(4.0, m.history_hours(half, MON_FRI, HOURS, set()))

    def test_hours_requests_are_their_duration(self):
        self.assertEqual(1.5, m.history_hours(item(Units={"Value": "Hours"}, LeaveDuration=1.5), MON_FRI, HOURS, set()))


class ValtoRemaining(unittest.TestCase):

    def test_entitlement_less_approved_pending_and_awaiting_cancellation_of_deducting_types(self):
        ent = {"BaseEntitlement": 25, "CarryForward": 3, "PurchasedLeave": 0}
        reqs = [item(LeaveDuration=5),
                item(LeaveDuration=1, Status={"Value": "Pending"}),
                item(LeaveDuration=0.5, RequestCancelled_x003f_=True),
                item(LeaveDuration=2, Status={"Value": "Cancelled"}),
                item(LeaveDuration=3, Status={"Value": "Rejected"}),
                item(LeaveDuration=4, Request_x0020_Type={"Value": "Sick Leave"})]
        self.assertEqual(21.5, m.valto_remaining(ent, reqs, {"Holiday Request"}))

    def test_request_year_is_valtos_entitlement_year(self):
        self.assertEqual(2026, m.request_year(item(Entitlement_x0020_Year={"Value": "2026.00000000000"})))
        self.assertEqual(2026, m.request_year(item(Entitlement_x0020_Year=None)))


class Names(unittest.TestCase):

    def test_request_name_matches_the_plugin(self):
        self.assertEqual("Ann Lee - 02/03/2026 to 06/03/2026", m.request_name("Ann Lee", "2026-03-02", "2026-03-06"))
        self.assertEqual("Ann Lee - 02/03/2026", m.request_name("Ann Lee", "2026-03-02", "2026-03-02"))

    def test_misspelt_valto_type_is_corrected(self):
        self.assertEqual("Authorised Absence", m.type_name(item(Request_x0020_Type={"Value": "Authorised Absense"})))


if __name__ == "__main__":
    unittest.main()
