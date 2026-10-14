"""DHA-8 - Recording time against a stopped project shows a prominent warning.

Acceptance criteria (Basecamp):
  - Project on stop: time entry is SAVED, and a red warning shows when the project is selected and when saved.
  - Project not on stop: no warning.
  - Project taken off stop: no warning.
  - Visual only - no blocking.

Covered here: the Dynamics form script (run in Node against a fake form, using the version DEPLOYED in dev),
its registration on the time entry form, and the data the desktop app relies on (planners can read the flag,
saving is never blocked). The desktop app's banner/dialog are UI and are in the manual checklist.
"""
import base64
import json
import os
import shutil
import subprocess
import tempfile
import unittest

from dvtest import DataverseTestCase, JESSICA, TEST_PROJECT, call, get, rows, error_text

TIME_ENTRY_FORM = "c682745b-4024-4f1b-bf66-75dadd8300b8"
WEB_RESOURCE = "fwp_projectOnStopWarning"
HARNESS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "js", "onstop_warning.harness.js")


class Dha08FormScript(DataverseTestCase):
    """The deployed web resource, exercised case by case in Node."""

    @classmethod
    def setUpClass(cls):
        wr = rows(f"webresourceset?$select=content&$filter=name eq '{WEB_RESOURCE}'")
        cls.script = base64.b64decode(wr[0]["content"]).decode("utf-8") if wr else None
        cls.results = []
        node = shutil.which("node")
        if cls.script and node:
            with tempfile.TemporaryDirectory() as tmp:
                path = os.path.join(tmp, "deployed.js")
                open(path, "w", encoding="utf-8").write(cls.script)
                out = subprocess.run([node, HARNESS, path], capture_output=True, text=True, timeout=60)
                cls.results = [json.loads(line) for line in out.stdout.splitlines() if line.startswith("{")]

    def test_web_resource_is_deployed(self):
        self.assertIsNotNone(self.script, f"{WEB_RESOURCE} not found in dev")

    def test_form_script_cases(self):
        if not shutil.which("node"):
            self.skipTest("node not installed")
        self.assertGreaterEqual(len(self.results), 9, "harness didn't run all cases")
        for r in self.results:
            with self.subTest(case=r["name"]):
                self.assertTrue(r["ok"], r.get("error"))

    def test_script_is_registered_on_the_time_entry_form(self):
        x = get(f"systemforms({TIME_ENTRY_FORM})?$select=formxml")["formxml"]
        self.assertIn(f'name="{WEB_RESOURCE}"', x, "library not added to the form")
        self.assertIn('functionName="ProjectOnStopWarning.onLoad"', x)
        self.assertIn('attribute="fwp_project"', x)
        self.assertIn('functionName="ProjectOnStopWarning.onProjectChange"', x)
        # the existing locking script must still be there
        self.assertIn('fwp_limitTimeEntryEditing', x)


class Dha08Data(DataverseTestCase):
    """What the desktop app and form script depend on."""

    @classmethod
    def setUpClass(cls):
        cls.original = get(f"msdyn_projects({TEST_PROJECT})?$select=fwp_onstop").get("fwp_onstop")
        cls.created = []

    @classmethod
    def tearDownClass(cls):
        call("PATCH", f"msdyn_projects({TEST_PROJECT})", {"fwp_onstop": bool(cls.original)})
        for tid in cls.created:  # test data only
            call("DELETE", f"fwp_timeentries({tid})")

    def stop(self, value):
        self.assertLess(call("PATCH", f"msdyn_projects({TEST_PROJECT})", {"fwp_onstop": value})[0], 300)

    def test_planner_sees_stopped_project_as_on_stop(self):
        self.stop(True)
        self.assertTrue(get(f"msdyn_projects({TEST_PROJECT})?$select=fwp_onstop", user=JESSICA)["fwp_onstop"])

    def test_planner_sees_project_taken_off_stop_as_not_on_stop(self):
        self.stop(True)
        self.stop(False)
        self.assertFalse(get(f"msdyn_projects({TEST_PROJECT})?$select=fwp_onstop", user=JESSICA)["fwp_onstop"])

    def test_desktop_project_list_query_works_for_a_planner(self):
        # Same columns as ProjectService.GetProjects; column security must not break it for non-finance users
        cols = "msdyn_subject,isc_projectnumbernew,_msdyn_customer_value,isc_discipline,statuscode,fwp_onstop"
        result = get(f"msdyn_projects?$select={cols}&$filter=statuscode ne 192350001&$top=50", user=JESSICA)["value"]
        self.assertGreater(len(result), 0)

    def test_saving_time_against_a_stopped_project_is_not_blocked(self):
        self.stop(True)
        s, d, _ = __import__("dv").call("POST", "fwp_timeentries", {
            "fwp_date": "2027-02-01", "fwp_decimalhours": 1.5, "fwp_durationhours": 1, "fwp_minutes": 30,
            "fwp_notes": "ZZTEST DHA-8", "fwp_category": 1, "fwp_classification": 800470000, "fwp_chargerate": 800470000,
            "fwp_Project@odata.bind": f"/msdyn_projects({TEST_PROJECT})", "fwp_TeamMember@odata.bind": f"/systemusers({JESSICA})"},
            extra={"Prefer": "return=representation"})
        self.assertLess(s, 300, f"time entry against a stopped project was blocked: {error_text(d)}")
        self.created.append(d["fwp_timeentryid"])


if __name__ == "__main__":
    unittest.main()
