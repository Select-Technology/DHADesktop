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


def flow_api(path, method="GET"):
    tok = dv.az_token("https://service.flow.microsoft.com/")
    url = f"https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple/environments/{DEV_ENVIRONMENT}/{path}"
    sep = "&" if "?" in url else "?"
    req = urllib.request.Request(url + sep + "api-version=2016-11-01", method=method, data=b"{}" if method == "POST" else None,
                                 headers={"Authorization": "Bearer " + tok, "Content-Type": "application/json"})
    txt = urllib.request.urlopen(req, timeout=60).read()
    return json.loads(txt) if txt else None


def intended_approver(request_id):
    """Who a request email is meant for: the request's approver, or HR when it has none."""
    r = get(f"{entity_set('fwp_leaverequest')}({request_id})?$select=_fwp_approver_value&$expand=fwp_Approver($select=internalemailaddress)")
    return (r.get("fwp_Approver") or {}).get("internalemailaddress") or env_value("fwp_LeaveApproverEmail")


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
            # Both must be sent, each with its own event. Power Automate doesn't guarantee run order for events a second apart.
            self.assertEqual(["Approved", "Requested"], sorted(events))
            jessica_email = get(f"systemusers({JESSICA})?$select=internalemailaddress")["internalemailaddress"]
            for event, to, subject in sent:
                with self.subTest(event=event):
                    self.assertEqual(self.override, to, "redirect not applied")
                    intended = intended_approver(rid) if event == "Requested" else jessica_email
                    self.assertTrue(subject.startswith(f"[TEST - for {intended}]"), subject)
        finally:
            call("PATCH", f"{self.LR}({rid})", {"fwp_status": CANCELLED})
            call("DELETE", f"{self.LR}({rid})")
            delete_time_entries("LV-" + rid.replace("-", ""))

    def test_reminder_goes_to_each_approver_once(self):
        s, d, _ = dv.call("POST", self.LR, {"fwp_Employee@odata.bind": f"/systemusers({JESSICA})", "fwp_startdate": "2027-10-05",
                                            "fwp_requestercomments": "ZZTEST reminder"}, extra={"Prefer": "return=representation"})
        self.assertLess(s, 300)
        rid = d["fwp_leaverequestid"]
        try:
            wid = self.flows["Leave - Approval reminder"]["workflowid"]
            started = time.strftime("%Y-%m-%dT%H:%M:%S", time.gmtime(time.time() - 2))
            flow_api(f"flows/{wid}/triggers/Weekdays_at_10/run", method="POST")
            deadline, run = time.time() + 180, None
            while time.time() < deadline and run is None:
                time.sleep(10)
                run = next((r for r in flow_api(f"flows/{wid}/runs")["value"] if r["properties"]["startTime"] >= started
                            and r["properties"]["status"] in ("Succeeded", "Failed")), None)
            self.assertIsNotNone(run, "reminder didn't run within 3 minutes")
            self.assertEqual("Succeeded", run["properties"]["status"])
            subjects = []
            for r in flow_api(f"flows/{wid}/runs/{run['name']}/actions/Email_approver/repetitions")["value"]:
                msg = json.loads(urllib.request.urlopen(r["properties"]["inputsLink"]["uri"], timeout=60).read())["parameters"]
                self.assertEqual(self.override, msg["emailMessage/To"], "redirect not applied")
                subjects.append(msg["emailMessage/Subject"])
            intended = [x.split("]")[0] for x in subjects]
            self.assertEqual(len(intended), len(set(intended)), "an approver got more than one reminder")
            self.assertIn(f"[TEST - for {intended_approver(rid)}", intended)
        finally:
            call("PATCH", f"{self.LR}({rid})", {"fwp_status": CANCELLED})
            call("DELETE", f"{self.LR}({rid})")


if __name__ == "__main__":
    unittest.main()
