"""DHA-7 - Finance can place a project on stop and take it off stop.

Acceptance criteria (Basecamp):
  - A finance user can place a project on stop; the project records it; the change is in the audit history.
  - A finance user can take it off stop; the stop is cleared; the change is in the audit history.
  - A project manager (not finance) has no control to set or clear the stop.
"""
import json
import time
import unittest

from dvtest import (DataverseTestCase, JESSICA, TEST_PROJECT, FMT, call, get, rows, error_text)

FINANCE_PROFILE = "DHA - Project On Stop: Finance"
READ_PROFILE = "DHA - Project On Stop: Read"
PROJECT_FORMS = {"105ef076-a72a-4382-bde5-35e3c3d9db40": "Information", "b274aa3d-43af-f011-bbd3-6045bdf1c876": "Project - DHA1"}


class Dha07ProjectOnStop(DataverseTestCase):

    @classmethod
    def setUpClass(cls):
        cls.original = get(f"msdyn_projects({TEST_PROJECT})?$select=fwp_onstop").get("fwp_onstop")

    @classmethod
    def tearDownClass(cls):
        call("PATCH", f"msdyn_projects({TEST_PROJECT})", {"fwp_onstop": bool(cls.original)})

    def set_on_stop(self, value, user=None):
        return call("PATCH", f"msdyn_projects({TEST_PROJECT})", {"fwp_onstop": value}, user)

    # ---- configuration ----

    def test_column_is_a_secured_audited_yes_no_defaulting_to_no(self):
        a = get("EntityDefinitions(LogicalName='msdyn_project')/Attributes(LogicalName='fwp_onstop')"
                "/Microsoft.Dynamics.CRM.BooleanAttributeMetadata?$select=IsSecured,IsAuditEnabled,DefaultValue,DisplayName")
        self.assertTrue(a["IsSecured"], "only column security can stop non-finance users editing it")
        self.assertTrue(a["IsAuditEnabled"]["Value"], "AC: the change must be in the audit history")
        self.assertFalse(a["DefaultValue"])
        self.assertEqual("On Stop", a["DisplayName"]["UserLocalizedLabel"]["Label"])

    def test_project_auditing_is_on(self):
        self.assertTrue(get("organizations?$select=isauditenabled")["value"][0]["isauditenabled"])
        self.assertTrue(get("EntityDefinitions(LogicalName='msdyn_project')?$select=IsAuditEnabled")["IsAuditEnabled"]["Value"])

    def test_on_stop_is_on_both_project_forms(self):
        for form_id, name in PROJECT_FORMS.items():
            with self.subTest(form=name):
                x = get(f"systemforms({form_id})?$select=formxml")["formxml"]
                self.assertIn('datafieldname="fwp_onstop"', x)

    def _profile_permission(self, profile):
        p = rows(f"fieldsecurityprofiles?$select=fieldsecurityprofileid&$filter=name eq '{profile}'")
        self.assertEqual(1, len(p), f"profile {profile} missing")
        perms = rows(f"fieldpermissions?$select=canread,cancreate,canupdate&$filter=_fieldsecurityprofileid_value eq "
                     f"{p[0]['fieldsecurityprofileid']} and attributelogicalname eq 'fwp_onstop'")
        self.assertEqual(1, len(perms))
        return p[0]["fieldsecurityprofileid"], perms[0]

    def test_finance_profile_can_read_create_and_update(self):
        _, perm = self._profile_permission(FINANCE_PROFILE)
        self.assertEqual((4, 4, 4), (perm["canread"], perm["cancreate"], perm["canupdate"]))

    def test_read_profile_is_read_only(self):
        _, perm = self._profile_permission(READ_PROFILE)
        self.assertEqual((4, 0, 0), (perm["canread"], perm["cancreate"], perm["canupdate"]))

    def test_everyone_is_in_the_read_profile_via_the_root_business_unit_team(self):
        pid, _ = self._profile_permission(READ_PROFILE)
        root = rows("businessunits?$select=businessunitid&$filter=_parentbusinessunitid_value eq null")[0]["businessunitid"]
        root_team = rows(f"teams?$select=teamid&$filter=isdefault eq true and _businessunitid_value eq {root}")[0]["teamid"]
        teams = [t["teamid"] for t in get(f"fieldsecurityprofiles({pid})/teamprofiles_association?$select=teamid")["value"]]
        self.assertIn(root_team, teams)

    def test_finance_profile_has_members(self):
        pid, _ = self._profile_permission(FINANCE_PROFILE)
        users = get(f"fieldsecurityprofiles({pid})/systemuserprofiles_association?$select=fullname")["value"]
        self.assertGreater(len(users), 0, "nobody could set the stop")

    # ---- behaviour ----

    def test_project_manager_can_see_the_stop(self):
        self.assertLess(self.set_on_stop(True)[0], 300)
        d = get(f"msdyn_projects({TEST_PROJECT})?$select=fwp_onstop", user=JESSICA)
        self.assertTrue(d["fwp_onstop"], "a PM must be able to read the flag, or they'd never see the warning")

    def test_project_manager_cannot_place_a_project_on_stop(self):
        self.set_on_stop(False)
        s, d = self.set_on_stop(True, user=JESSICA)
        self.assertGreaterEqual(s, 400)
        self.assertIn("secured field", error_text(d))
        self.assertFalse(get(f"msdyn_projects({TEST_PROJECT})?$select=fwp_onstop")["fwp_onstop"])

    def test_project_manager_cannot_take_a_project_off_stop(self):
        self.set_on_stop(True)
        s, d = self.set_on_stop(False, user=JESSICA)
        self.assertGreaterEqual(s, 400)
        self.assertIn("secured field", error_text(d))
        self.assertTrue(get(f"msdyn_projects({TEST_PROJECT})?$select=fwp_onstop")["fwp_onstop"])

    def test_placing_and_clearing_a_stop_is_recorded_and_audited(self):
        self.set_on_stop(False)
        started = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(time.time() - 5))
        self.assertLess(self.set_on_stop(True)[0], 300)
        self.assertTrue(get(f"msdyn_projects({TEST_PROJECT})?$select=fwp_onstop")["fwp_onstop"])
        self.assertLess(self.set_on_stop(False)[0], 300)
        self.assertFalse(get(f"msdyn_projects({TEST_PROJECT})?$select=fwp_onstop")["fwp_onstop"])

        # Audit records are written asynchronously - poll briefly
        changes = []
        for _ in range(10):
            audits = rows(f"audits?$select=changedata,createdon&$filter=_objectid_value eq {TEST_PROJECT} and createdon ge {started}"
                          f"&$orderby=createdon asc")
            changes = [c["newValue"] for a in audits for c in (json.loads(a["changedata"] or "{}").get("changedAttributes") or [])
                       if c["logicalName"] == "fwp_onstop"]
            if changes[-2:] == ["True", "False"]:
                break
            time.sleep(3)
        self.assertEqual(["True", "False"], changes[-2:], "placing and clearing the stop should both be in the audit history")

    @unittest.skip("No non-admin finance user in dev has the base roles needed to update a project "
                   "(Julie Thorne lacks prvReadUser; Dynamics Automation lacks msdyn_actual read). "
                   "Finance write access is covered by the profile-permission tests above.")
    def test_finance_user_can_place_and_clear_a_stop(self):
        pass


if __name__ == "__main__":
    unittest.main()
