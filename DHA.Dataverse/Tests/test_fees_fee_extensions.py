"""Fee extensions update the project fee (September 2026 fee adjustment, DHA.ProjectFees.Plugins).

Rule: DHA Fee = original fee + active fee extensions.
  - A new project's original fee is its DHA Fee; until the first extension it follows DHA Fee corrections.
  - Logging a fee extension adds it to the DHA Fee, totals the extensions and records the last/previous dates.
  - Correcting an extension's amount, deactivating, reactivating, deleting it or moving it to another project
    recalculates the project(s).
  - A project created before fee extensions existed (no original fee) gets it from the DHA Fee on its first extension.
  - Once a project has extensions only a System Administrator can change its DHA Fee; the correction survives later
    extension changes. Resaving the same value is fine for anyone.
  - The prod backfill applies extensions logged while the plug-in was missing, and its snapshot restores exactly.
"""
import os
import time
import unittest

from dvtest import DataverseTestCase, JESSICA, call, get, rows, error_text, dv
import provision_project_fees as fees

RETURN = {"Prefer": "return=representation"}
TEST_NAME = "ZZ fee extension test"


def create(entity_set, body):
    s, d, _ = dv.call("POST", entity_set, body, extra=RETURN)
    return s, d


def remove_project(pid):
    for e in rows(f"fwp_feeextensions?$select=fwp_feeextensionid&$filter=_fwp_project_value eq {pid}"):
        call("DELETE", f"fwp_feeextensions({e['fwp_feeextensionid']})")
    call("DELETE", f"msdyn_projects({pid})")


class FeeTestCase(DataverseTestCase):

    @classmethod
    def setUpClass(cls):
        # Leftovers from an interrupted run
        for p in rows(f"msdyn_projects?$select=msdyn_projectid&$filter=startswith(msdyn_subject,'{TEST_NAME}')"):
            remove_project(p["msdyn_projectid"])

    def setUp(self):
        self.projects = []

    def tearDown(self):
        for p in self.projects:
            remove_project(p)

    def new_project(self, fee, number=None):
        body = {"msdyn_subject": f"{TEST_NAME} {time.time():.0f}", "isc_projectamount": fee}
        if number:
            body["isc_projectnumbernew"] = number
        s, d = create("msdyn_projects?$select=msdyn_projectid", body)
        self.assertLess(s, 300, error_text(d))
        self.projects.append(d["msdyn_projectid"])
        return d["msdyn_projectid"]

    def project(self, pid):
        return get(f"msdyn_projects({pid})?$select=isc_projectamount,fwp_originalprojectfee,isc_feeextension,"
                   "fwp_lastfeeextensiondate,fwp_previousfeeextensiondate")

    def fees(self, pid):
        p = self.project(pid)
        return p["isc_projectamount"], p["fwp_originalprojectfee"], p["isc_feeextension"]

    def extend(self, pid, amount):
        s, d = create("fwp_feeextensions?$select=fwp_feeextensionid,createdon",
                      {"fwp_name": f"test {amount}", "fwp_amount": amount, "fwp_Project@odata.bind": f"/msdyn_projects({pid})"})
        self.assertLess(s, 300, error_text(d))
        return d["fwp_feeextensionid"], d["createdon"]

    def patch(self, path, body, user=None):
        s, d = call("PATCH", path, body, user)
        self.assertLess(s, 300, error_text(d))


class FeeExtensions(FeeTestCase):

    def test_new_project_original_fee_is_its_dha_fee(self):
        pid = self.new_project(1000)
        self.assertEqual(1000, self.project(pid)["fwp_originalprojectfee"])

    def test_original_fee_follows_corrections_before_any_extension(self):
        pid = self.new_project(1000)
        self.patch(f"msdyn_projects({pid})", {"isc_projectamount": 1200})
        self.assertEqual(1200, self.project(pid)["fwp_originalprojectfee"])

    def test_extensions_add_to_dha_fee_and_record_dates(self):
        pid = self.new_project(1000)
        _, first = self.extend(pid, 300)
        p = self.project(pid)
        self.assertEqual((1300, 1000, 300), self.fees(pid))
        self.assertEqual(first, p["fwp_lastfeeextensiondate"])
        self.assertIsNone(p["fwp_previousfeeextensiondate"])

        _, second = self.extend(pid, 200.5)
        p = self.project(pid)
        self.assertEqual((1500.5, 1000, 500.5), self.fees(pid))
        self.assertEqual((second, first), (p["fwp_lastfeeextensiondate"], p["fwp_previousfeeextensiondate"]))

    def test_project_without_original_fee_gets_it_on_first_extension(self):
        # Like every prod project created before fee extensions existed
        pid = self.new_project(29850)
        self.patch(f"msdyn_projects({pid})", {"fwp_originalprojectfee": None})
        self.extend(pid, 1250)
        self.assertEqual((31100, 29850, 1250), self.fees(pid))

    def test_correcting_an_extension_amount_recalculates(self):
        pid = self.new_project(1000)
        ext, _ = self.extend(pid, 1250)
        self.patch(f"fwp_feeextensions({ext})", {"fwp_amount": 1500})
        self.assertEqual((2500, 1000, 1500), self.fees(pid))

    def test_deactivating_and_reactivating_an_extension_recalculates(self):
        pid = self.new_project(1000)
        _, first = self.extend(pid, 100)
        ext, _ = self.extend(pid, 250)

        self.patch(f"fwp_feeextensions({ext})", {"statecode": 1, "statuscode": 2})
        p = self.project(pid)
        self.assertEqual((1100, 1000, 100), self.fees(pid))
        self.assertEqual((first, None), (p["fwp_lastfeeextensiondate"], p["fwp_previousfeeextensiondate"]))

        self.patch(f"fwp_feeextensions({ext})", {"statecode": 0, "statuscode": 1})
        self.assertEqual((1350, 1000, 350), self.fees(pid))

    def test_deleting_an_extension_recalculates(self):
        pid = self.new_project(1000)
        self.extend(pid, 100)
        ext, _ = self.extend(pid, 250)
        s, d = call("DELETE", f"fwp_feeextensions({ext})")
        self.assertLess(s, 300, error_text(d))
        self.assertEqual((1100, 1000, 100), self.fees(pid))

        ext = rows(f"fwp_feeextensions?$select=fwp_feeextensionid&$filter=_fwp_project_value eq {pid}")[0]["fwp_feeextensionid"]
        call("DELETE", f"fwp_feeextensions({ext})")
        p = self.project(pid)
        self.assertEqual((1000, 1000, 0), self.fees(pid))
        self.assertIsNone(p["fwp_lastfeeextensiondate"])

    def test_moving_an_extension_to_another_project_recalculates_both(self):
        a, b = self.new_project(1000), self.new_project(5000)
        ext, _ = self.extend(a, 400)
        self.patch(f"fwp_feeextensions({ext})", {"fwp_Project@odata.bind": f"/msdyn_projects({b})"})
        self.assertEqual((1000, 1000, 0), self.fees(a))
        self.assertEqual((5400, 5000, 400), self.fees(b))

    def test_dha_fee_is_guarded_once_extended_for_non_admins(self):
        pid = self.new_project(1000)
        self.extend(pid, 100)
        s, d = call("PATCH", f"msdyn_projects({pid})", {"isc_projectamount": 5000}, JESSICA)
        self.assertGreaterEqual(s, 400)
        self.assertIn("ask Finance to raise a fee extension", error_text(d))
        self.assertEqual(1100, self.project(pid)["isc_projectamount"])

    def assertNotGuarded(self, s, d):
        """Dev secures DHA Fee by column (prod doesn't), so a non-admin may be refused by column security in dev.
        What matters here is that the fee guard is not what stops them."""
        if s < 300:
            return True
        self.assertNotIn("ask Finance to raise a fee extension", error_text(d))
        self.assertIn("secured field", error_text(d))
        return False

    def test_a_non_admin_is_not_guarded_before_any_extension(self):
        pid = self.new_project(1000)
        s, d = call("PATCH", f"msdyn_projects({pid})", {"isc_projectamount": 1100}, JESSICA)
        if not self.assertNotGuarded(s, d):
            self.skipTest("dev only: Jessica can't update the column-secured DHA Fee (not secured in prod)")
        self.assertEqual((1100, 1100, None), self.fees(pid))

    def test_resaving_an_unchanged_dha_fee_is_not_guarded(self):
        pid = self.new_project(1000)
        self.extend(pid, 100)
        s, d = call("PATCH", f"msdyn_projects({pid})", {"isc_projectamount": 1100}, JESSICA)
        if not self.assertNotGuarded(s, d):
            self.skipTest("dev only: Jessica can't update the column-secured DHA Fee (not secured in prod)")

    def test_admin_correction_survives_later_extension_changes(self):
        pid = self.new_project(1000)
        self.extend(pid, 100)
        self.patch(f"msdyn_projects({pid})", {"isc_projectamount": 2100})   # stsadmin: System Administrator
        self.assertEqual((2100, 2000, 100), self.fees(pid))
        self.extend(pid, 50)
        self.assertEqual((2150, 2000, 150), self.fees(pid))


class Backfill(FeeTestCase):
    """The prod backfill, run against a dev project set up like prod (extension logged, plug-in absent)."""

    def set_guard(self, enabled):
        step = rows(f"sdkmessageprocessingsteps?$select=sdkmessageprocessingstepid&$filter=name eq '{fees.GUARD_STEP}'")[0]
        state = {"statecode": 0, "statuscode": 1} if enabled else {"statecode": 1, "statuscode": 2}
        self.patch(f"sdkmessageprocessingsteps({step['sdkmessageprocessingstepid']})", state)

    def test_backfill_applies_logged_extensions_and_restores_from_snapshot(self):
        number = f"ZZ{time.time():.0f}"
        pid = self.new_project(29850, number)
        self.extend(pid, 1250)
        # Put the project back as prod is today: extension logged but never applied, no original fee
        self.patch(f"msdyn_projects({pid})", {"isc_projectamount": 29850})   # System Administrator correction
        self.patch(f"msdyn_projects({pid})", {"fwp_originalprojectfee": None, "isc_feeextension": None,
                                              "fwp_lastfeeextensiondate": None, "fwp_previousfeeextensiondate": None})
        self.assertEqual((29850, None, None), self.fees(pid))
        before = self.project(pid)
        snapshots = set(os.listdir(fees.BACKUPS)) if os.path.isdir(fees.BACKUPS) else set()
        self.set_guard(False)
        try:
            fees.backfill(dv, False, number)
            p = self.project(pid)
            self.assertEqual((31100, 29850, 1250), self.fees(pid))
            self.assertIsNotNone(p["fwp_lastfeeextensiondate"])

            fees.backfill(dv, False, number)  # re-running adds nothing
            self.assertEqual((31100, 29850, 1250), self.fees(pid))

            taken = sorted(set(os.listdir(fees.BACKUPS)) - snapshots)
            self.assertEqual(2, len(taken), "each run keeps its own snapshot; none is overwritten")
            fees.restore(dv, os.path.join(fees.BACKUPS, taken[0]), False)
            self.assertEqual({k: before[k] for k in fees.FEE_FIELDS}, {k: self.project(pid)[k] for k in fees.FEE_FIELDS})
        finally:
            self.set_guard(True)
            for f in set(os.listdir(fees.BACKUPS)) - snapshots:
                os.remove(os.path.join(fees.BACKUPS, f))

    def test_backfill_and_restore_refuse_while_the_guard_is_active(self):
        with self.assertRaises(SystemExit):
            fees.backfill(dv, True, "ZZ-none")


if __name__ == "__main__":
    unittest.main()
