"""DHA-9 / leave parity N4 - approved absence on the shared Outlook leave calendar ("Leave - Outlook calendar" flow).

In dev the flow writes to stsadmin's own Calendar (fwp_LeaveCalendarId = "default") and, because fwp_LeaveEmailOverride is
set, invites nobody. Checks what the flow actually sent to Outlook, from its run history.
"""
import json
import time
import unittest
import urllib.request

from dvtest import DataverseTestCase, JESSICA, APPROVED, CANCELLED, call, get, rows, entity_set, delete_time_entries
from test_dha09_leave_emails import flow_api
import dv  # noqa: E402 - path set up by dvtest


def leave_type(name):
    return rows(f"fwp_leavetypes?$select=fwp_leavetypeid&$filter=fwp_name eq '{name}' and statecode eq 0")[0]["fwp_leavetypeid"]


class Dha09Calendar(DataverseTestCase):

    @classmethod
    def setUpClass(cls):
        cls.LR = entity_set("fwp_leaverequest")
        cls.flow = rows("workflows?$select=workflowid,statecode&$filter=category eq 5 and name eq 'Leave - Outlook calendar'")
        cls.made = []

    @classmethod
    def tearDownClass(cls):
        for rid in cls.made:
            call("PATCH", f"{cls.LR}({rid})", {"fwp_status": CANCELLED})
        time.sleep(30)  # let the flow take the events off the calendar before the rows go
        for rid in cls.made:
            call("DELETE", f"{cls.LR}({rid})")
            delete_time_entries("LV-" + rid.replace("-", ""))

    def create(self, start, end=None, **extra):
        body = {"fwp_Employee@odata.bind": f"/systemusers({JESSICA})", "fwp_startdate": start, "fwp_requestercomments": "ZZTEST calendar"}
        if end:
            body["fwp_enddate"] = end
        body.update(extra)
        s, d, _ = dv.call("POST", self.LR, body, extra={"Prefer": "return=representation"})
        self.assertLess(s, 300, d)
        self.made.append(d["fwp_leaverequestid"])
        return d["fwp_leaverequestid"]

    def event_id(self, rid):
        return get(f"{self.LR}({rid})?$select=fwp_calendareventid")["fwp_calendareventid"]

    def wait_for_event_id(self, rid, condition, what):
        deadline = time.time() + 180
        while time.time() < deadline:
            value = self.event_id(rid)
            if condition(value):
                return value
            time.sleep(10)
        self.fail(f"calendar event id never {what} within 3 minutes (now {self.event_id(rid)!r})")

    def created_event(self, event_id):
        """What the flow sent to Outlook for the event it saved as event_id."""
        wid = self.flow[0]["workflowid"]
        for run in flow_api(f"flows/{wid}/runs")["value"][:15]:
            actions = {a["name"]: a for a in flow_api(f"flows/{wid}/runs/{run['name']}/actions")["value"]}
            create = actions.get("Create_event")
            if not create or create["properties"]["status"] != "Succeeded":
                continue
            out = json.loads(urllib.request.urlopen(create["properties"]["outputsLink"]["uri"], timeout=60).read())
            if out.get("body", {}).get("id") == event_id:
                return json.loads(urllib.request.urlopen(create["properties"]["inputsLink"]["uri"], timeout=60).read())["parameters"]
        self.fail("couldn't find the flow run that created the event")

    def test_flow_is_on(self):
        self.assertTrue(self.flow, "flow missing")
        self.assertEqual(1, self.flow[0]["statecode"])

    def test_approved_leave_is_on_the_calendar_moves_with_it_and_goes_when_cancelled(self):
        rid = self.create("2027-10-06", **{"fwp_LeaveType@odata.bind": f"/fwp_leavetypes({leave_type('Medical')})"})
        call("PATCH", f"{self.LR}({rid})", {"fwp_status": APPROVED})
        first = self.wait_for_event_id(rid, bool, "set after approval")

        sent = self.created_event(first)
        jessica = get(f"systemusers({JESSICA})?$select=fullname")["fullname"]
        self.assertEqual(f"Out of Office - {jessica}", sent["item/subject"])
        self.assertNotIn("Medical", json.dumps(sent), "the leave type reached the shared calendar")
        self.assertEqual("oof", sent["item/showAs"])
        self.assertTrue(sent["item/isAllDay"])
        self.assertEqual(("2027-10-06T00:00:00", "2027-10-07T00:00:00"), (sent["item/start"], sent["item/end"]))
        self.assertFalse(sent.get("item/requiredAttendees"), "dev invited real people")

        # HR change the dates: the event moves (new event, new id)
        call("PATCH", f"{self.LR}({rid})", {"fwp_enddate": "2027-10-07"})
        moved = self.wait_for_event_id(rid, lambda v: v and v != first, "replaced after the dates changed")
        self.assertEqual("2027-10-08T00:00:00", self.created_event(moved)["item/end"])

        # cancelled: off the calendar
        call("PATCH", f"{self.LR}({rid})", {"fwp_status": CANCELLED})
        self.wait_for_event_id(rid, lambda v: not v, "cleared after cancelling")

    def test_part_day_uses_its_times(self):
        rid = self.create("2027-10-11", fwp_starttime="13:30", fwp_endtime="15:00")
        call("PATCH", f"{self.LR}({rid})", {"fwp_status": APPROVED})
        sent = self.created_event(self.wait_for_event_id(rid, bool, "set after approval"))
        self.assertFalse(sent["item/isAllDay"])
        self.assertEqual(("2027-10-11T13:30:00", "2027-10-11T15:00:00"), (sent["item/start"], sent["item/end"]))

    def test_working_from_home_stays_off_the_calendar(self):
        rid = self.create("2027-10-12", **{"fwp_LeaveType@odata.bind": f"/fwp_leavetypes({leave_type('Working From Home')})"})
        call("PATCH", f"{self.LR}({rid})", {"fwp_status": APPROVED})
        time.sleep(60)
        self.assertFalse(self.event_id(rid), "working from home was put on the Out of Office calendar")


if __name__ == "__main__":
    unittest.main()
