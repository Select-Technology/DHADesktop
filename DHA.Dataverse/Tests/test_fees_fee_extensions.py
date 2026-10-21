"""Fee extensions update the project fee (September 2026 fee adjustment, DHA.ProjectFees.Plugins).

  - A new project's original fee is its DHA Fee; until the first extension it follows DHA Fee corrections.
  - Logging a fee extension adds it to the DHA Fee, totals the extensions and records the last/previous dates.
  - A project created before fee extensions existed (no original fee) gets it from the DHA Fee on its first extension.
  - Once a project has extensions its DHA Fee can't be changed by hand; resaving the same value is fine.
"""
import time
import unittest

from dvtest import DataverseTestCase, call, get, rows, error_text, dv

RETURN = {"Prefer": "return=representation"}
TEST_NAME = "ZZ fee extension test"


def create(entity_set, body):
    s, d, _ = dv.call("POST", entity_set, body, extra=RETURN)
    return s, d


class FeeExtensions(DataverseTestCase):

    @classmethod
    def setUpClass(cls):
        # Leftovers from an interrupted run
        for p in rows(f"msdyn_projects?$select=msdyn_projectid&$filter=startswith(msdyn_subject,'{TEST_NAME}')"):
            cls.remove(p["msdyn_projectid"])

    @staticmethod
    def remove(pid):
        for e in rows(f"fwp_feeextensions?$select=fwp_feeextensionid&$filter=_fwp_project_value eq {pid}"):
            call("DELETE", f"fwp_feeextensions({e['fwp_feeextensionid']})")
        call("DELETE", f"msdyn_projects({pid})")

    def setUp(self):
        self.projects = []

    def tearDown(self):
        for p in self.projects:
            self.remove(p)

    def new_project(self, fee):
        s, d = create("msdyn_projects?$select=msdyn_projectid",
                      {"msdyn_subject": f"{TEST_NAME} {time.time():.0f}", "isc_projectamount": fee})
        self.assertLess(s, 300, error_text(d))
        self.projects.append(d["msdyn_projectid"])
        return d["msdyn_projectid"]

    def project(self, pid):
        return get(f"msdyn_projects({pid})?$select=isc_projectamount,fwp_originalprojectfee,isc_feeextension,"
                   "fwp_lastfeeextensiondate,fwp_previousfeeextensiondate")

    def extend(self, pid, amount):
        s, d = create("fwp_feeextensions?$select=createdon",
                      {"fwp_name": f"test {amount}", "fwp_amount": amount, "fwp_Project@odata.bind": f"/msdyn_projects({pid})"})
        self.assertLess(s, 300, error_text(d))
        return d["createdon"]

    def test_new_project_original_fee_is_its_dha_fee(self):
        pid = self.new_project(1000)
        self.assertEqual(1000, self.project(pid)["fwp_originalprojectfee"])

    def test_original_fee_follows_corrections_before_any_extension(self):
        pid = self.new_project(1000)
        s, d = call("PATCH", f"msdyn_projects({pid})", {"isc_projectamount": 1200})
        self.assertLess(s, 300, error_text(d))
        self.assertEqual(1200, self.project(pid)["fwp_originalprojectfee"])

    def test_extensions_add_to_dha_fee_and_record_dates(self):
        pid = self.new_project(1000)
        first = self.extend(pid, 300)
        p = self.project(pid)
        self.assertEqual((1300, 1000, 300), (p["isc_projectamount"], p["fwp_originalprojectfee"], p["isc_feeextension"]))
        self.assertEqual(first, p["fwp_lastfeeextensiondate"])
        self.assertIsNone(p["fwp_previousfeeextensiondate"])

        second = self.extend(pid, 200.5)
        p = self.project(pid)
        self.assertEqual((1500.5, 1000, 500.5), (p["isc_projectamount"], p["fwp_originalprojectfee"], p["isc_feeextension"]))
        self.assertEqual((second, first), (p["fwp_lastfeeextensiondate"], p["fwp_previousfeeextensiondate"]))

    def test_project_without_original_fee_gets_it_on_first_extension(self):
        # Like every prod project created before fee extensions existed
        pid = self.new_project(29850)
        call("PATCH", f"msdyn_projects({pid})", {"fwp_originalprojectfee": None})
        self.assertIsNone(self.project(pid)["fwp_originalprojectfee"])

        self.extend(pid, 1250)
        p = self.project(pid)
        self.assertEqual((31100, 29850, 1250), (p["isc_projectamount"], p["fwp_originalprojectfee"], p["isc_feeextension"]))

    def test_dha_fee_is_guarded_once_extended(self):
        pid = self.new_project(1000)
        self.extend(pid, 100)
        s, d = call("PATCH", f"msdyn_projects({pid})", {"isc_projectamount": 5000})
        self.assertGreaterEqual(s, 400)
        self.assertIn("ask Finance to raise a fee extension", error_text(d))
        self.assertEqual(1100, self.project(pid)["isc_projectamount"])

        s, d = call("PATCH", f"msdyn_projects({pid})", {"isc_projectamount": 1100})
        self.assertLess(s, 300, "resaving the unchanged DHA Fee must not be blocked: " + error_text(d))


if __name__ == "__main__":
    unittest.main()
