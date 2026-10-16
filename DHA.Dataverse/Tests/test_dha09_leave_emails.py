"""DHA-9 (phase B) - leave notification emails ("Leave - Notifications" / "Leave - Approval reminder" flows).

Checks the flows are on, that a request and its approval each produce exactly the right email, and that the
testing redirect (fwp_LeaveEmailOverride) is honoured so dev never emails real staff.
"""
import json
import shutil
import subprocess
import time
import unittest
import urllib.request

from dvtest import DataverseTestCase, JESSICA, APPROVED, CANCELLED, call, get, rows, entity_set, delete_time_entries
import dv  # noqa: E402 - path set up by dvtest

DEV_ENVIRONMENT = "964277cb-bd95-ee45-8c9b-b5dbe4520aa8"


def flow_api(path):
    tok = dv.az_token("https://service.flow.microsoft.com/")
    url = f"https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple/environments/{DEV_ENVIRONMENT}/{path}"
    sep = "&" if "?" in url else "?"
    return json.loads(urllib.request.urlopen(urllib.request.Request(url + sep + "api-version=2016-11-01",
                                                                     headers={"Authorization": "Bearer " + tok})).read())


def env_value(schema):
    d = rows(f"environmentvariabledefinitions?$select=defaultvalue&$filter=schemaname eq '{schema}'"
             f"&$expand=environmentvariabledefinition_environmentvariablevalue($select=value)")[0]
    vals = d["environmentvariabledefinition_environmentvariablevalue"]
    return vals[0]["value"] if vals else d["defaultvalue"]


class Dha09Emails(DataverseTestCase):

    @classmethod
    def setUpClass(cls):
        cls.flows = {w["name"]: w for w in rows("workflows?$select=name,workflowid,statecode&$filter=category eq 5 and "
                                                "(name eq 'Leave - Notifications' or name eq 'Leave - Approval reminder')")}
        cls.override = env_value("fwp_LeaveEmailOverride")
        cls.LR = entity_set("fwp_leaverequest")

    def test_both_flows_exist_and_are_on(self):
        for name in ("Leave - Notifications", "Leave - Approval reminder"):
            with self.subTest(flow=name):
                self.assertIn(name, self.flows)
                self.assertEqual(1, self.flows[name]["statecode"], f"{name} is off")

    def test_dev_redirects_every_email(self):
        self.assertTrue(self.override, "fwp_LeaveEmailOverride must be set in dev so tests never email real staff")

    def _sent_emails_since(self, started, marker, expected):
        """Waits for notification runs after `started` that emailed about `marker` (a date in the subject).
        Returns [(event, to, subject)] in send order. Other tests' clean-up runs are ignored."""
        wid = self.flows["Leave - Notifications"]["workflowid"]
        deadline = time.time() + 180
        sent, seen = [], {}
        while time.time() < deadline:
            for r in flow_api(f"flows/{wid}/runs")["value"]:
                p = r["properties"]
                if p["startTime"] < started or r["name"] in seen or p["status"] not in ("Succeeded", "Failed"):
                    continue
                seen[r["name"]] = []
                for a in flow_api(f"flows/{wid}/runs/{r['name']}/actions")["value"]:
                    if a["name"].startswith("Email_") and a["properties"]["status"] != "Skipped":
                        msg = json.loads(urllib.request.urlopen(a["properties"]["inputsLink"]["uri"]).read())["parameters"]
                        if marker in msg["emailMessage/Subject"]:
                            self.assertEqual("Succeeded", a["properties"]["status"], f"{a['name']} failed")
                            seen[r["name"]].append((p["startTime"], a["name"][6:], msg["emailMessage/To"], msg["emailMessage/Subject"]))
            sent = sorted(x for v in seen.values() for x in v)
            if len(sent) >= expected:
                return [x[1:] for x in sent]
            time.sleep(10)
        self.fail(f"only {len(sent)} of {expected} expected emails were sent within 3 minutes")

    def test_request_and_approval_each_send_the_right_email_to_the_redirect(self):
        started = time.strftime("%Y-%m-%dT%H:%M:%S", time.gmtime(time.time() - 2))
        s, d, _ = __import__("dv").call("POST", self.LR, {"fwp_Employee@odata.bind": f"/systemusers({JESSICA})",
                                                          "fwp_startdate": "2027-10-04", "fwp_requestercomments": "ZZTEST email"},
                                        extra={"Prefer": "return=representation"})
        self.assertLess(s, 300)
        rid = d["fwp_leaverequestid"]
        try:
            time.sleep(1)  # approve almost immediately - the request email must still say "Requested"
            call("PATCH", f"{self.LR}({rid})", {"fwp_status": APPROVED, "fwp_approvercomments": "ZZTEST"})
            sent = self._sent_emails_since(started, "04/10/2027", expected=2)
            events = [e for e, _, _ in sent]
            self.assertEqual(["Requested", "Approved"], events)
            jessica_email = get(f"systemusers({JESSICA})?$select=internalemailaddress")["internalemailaddress"]
            for event, to, subject in sent:
                with self.subTest(event=event):
                    self.assertEqual(self.override, to, "redirect not applied")
                    intended = env_value("fwp_LeaveApproverEmail") if event == "Requested" else jessica_email
                    self.assertTrue(subject.startswith(f"[TEST - for {intended}]"), subject)
        finally:
            call("PATCH", f"{self.LR}({rid})", {"fwp_status": CANCELLED})
            call("DELETE", f"{self.LR}({rid})")
            delete_time_entries("LV-" + rid.replace("-", ""))


if __name__ == "__main__":
    unittest.main()
