"""Leave parity stage 5 (year-end): fwp_GenerateLeaveYear and fwp_CarryForwardLeave, run for Jessica only (EmployeeId) so
the rest of dev is untouched. Jessica has no 2026/2027 entitlements and no base entitlement in dev; both are put back.
"""
import unittest

from dvtest import JESSICA, APPROVED, call, get, rows, error_text, entity_set
from test_dha09_leave_requests import LeaveRequestTestCase


class Dha09ParityStage5YearEnd(LeaveRequestTestCase):

    @classmethod
    def setUpClass(cls):
        super().setUpClass()
        cls.LE = entity_set("fwp_leaveentitlement")
        cfg = rows(f"fwp_colleagueconfigurations?$select=fwp_colleagueconfigurationid,fwp_baseentitlement"
                   f"&$filter=_fwp_colleague_value eq {JESSICA} and statecode eq 0")[0]
        cls.cfg_id, cls.original_base = cfg["fwp_colleagueconfigurationid"], cfg["fwp_baseentitlement"]

    @classmethod
    def tearDownClass(cls):
        call("PATCH", f"fwp_colleagueconfigurations({cls.cfg_id})", {"fwp_baseentitlement": cls.original_base})
        super().tearDownClass()

    def setUp(self):
        if self.entitlements():
            self.skipTest("Jessica already has 2026/2027 entitlements in dev - these tests would change them")
        self.assertLess(call("PATCH", f"fwp_colleagueconfigurations({self.cfg_id})", {"fwp_baseentitlement": 25})[0], 300)

    def tearDown(self):
        for e in self.entitlements().values():
            call("DELETE", f"{self.LE}({e['fwp_leaveentitlementid']})")
        # each test's requests go now, so they don't count in the next test's balance
        LeaveRequestTestCase.tearDownClass.__func__(type(self))
        type(self).made = []

    def entitlements(self):
        found = rows(f"{self.LE}?$select=fwp_yearstart,fwp_entitlementdays,fwp_carriedforwarddays,fwp_carriedtonextyeardays,_ownerid_value"
                     f"&$filter=_fwp_employee_value eq {JESSICA} and fwp_yearstart ge 2026-01-01 and fwp_yearstart le 2027-01-01")
        return {int(e["fwp_yearstart"][:4]): e for e in found}

    def generate(self, year, user=None):
        return call("POST", "fwp_GenerateLeaveYear", {"Year": year, "EmployeeId": JESSICA}, user=user)

    def carry(self, year=2027, user=None):
        return call("POST", "fwp_CarryForwardLeave", {"Year": year, "EmployeeId": JESSICA}, user=user)

    def test_only_hr_can_run_year_end(self):
        for api in (self.generate(2027, user=JESSICA), self.carry(user=JESSICA)):
            self.assertGreaterEqual(api[0], 400)
            self.assertIn("Only HR", error_text(api[1]))

    def test_years_too_far_ahead_are_refused(self):
        s, d = self.generate(2031)
        self.assertGreaterEqual(s, 400)
        self.assertIn("too far ahead", error_text(d))

    def test_generate_creates_the_year_from_base_entitlement_once(self):
        s, d = self.generate(2027)
        self.assertLess(s, 300, d)
        self.assertEqual(1, d["Created"])
        row = self.entitlements()[2027]
        self.assertEqual(25, row["fwp_entitlementdays"])
        # Ownership passes to the employee only if they hold DHA-1, which no dev test user can (parity plan stage 1 notes),
        # so here it just must not be left with the HR person who ran it
        self.assertNotEqual(get("WhoAmI")["UserId"], row["_ownerid_value"])
        # safe to re-run
        s, d = self.generate(2027)
        self.assertEqual((0, 1), (d["Created"], len(self.entitlements())))
        self.assertIn("1 already set up", d["Report"])

    def test_no_base_entitlement_is_listed_not_created(self):
        call("PATCH", f"fwp_colleagueconfigurations({self.cfg_id})", {"fwp_baseentitlement": None})
        s, d = self.generate(2027)
        self.assertLess(s, 300, d)
        self.assertEqual(0, d["Created"])
        self.assertIn("No base entitlement", d["Report"])
        self.assertEqual({}, self.entitlements())

    def test_carry_forward_is_capped_counts_pending_and_can_be_rerun(self):
        self.generate(2026)
        self.generate(2027)
        old = self.entitlements()[2026]["fwp_leaveentitlementid"]
        call("PATCH", f"{self.LE}({old})", {"fwp_entitlementdays": 10})
        # 2 days approved, 1 pending, in December 2026 - 7 unused
        self.set_status(self.created("2026-12-14", "2026-12-15"), APPROVED)
        self.created("2026-12-16")

        s, d = self.carry()
        self.assertLess(s, 300, d)
        self.assertEqual(1, d["Carried"])
        self.assertIn("7 unused, 5 carried (2 over the limit of 5), including 1 still pending", d["Report"])
        e = self.entitlements()
        self.assertEqual((5, 5), (e[2026]["fwp_carriedtonextyeardays"], e[2027]["fwp_carriedforwarddays"]))

        # HR raise Jessica's limit for 2026, then run again: the figures are replaced, not added to
        call("PATCH", f"{self.LE}({old})", {"fwp_carryforwardoverride": 6})
        s, d = self.carry()
        self.assertIn("(1 over the override of 6)", d["Report"])
        e = self.entitlements()
        self.assertEqual((6, 6), (e[2026]["fwp_carriedtonextyeardays"], e[2027]["fwp_carriedforwarddays"]))
        s, d = self.carry()
        self.assertEqual(6, self.entitlements()[2027]["fwp_carriedforwarddays"])

        # and the 2027 balance shows it
        b = call("POST", "fwp_GetLeaveBalance", {"EmployeeId": JESSICA, "Year": 2027})[1]
        self.assertEqual(6, b["CarriedForwardDays"])

    def test_no_new_year_row_is_listed_and_nothing_carried(self):
        self.generate(2026)
        s, d = self.carry()
        self.assertLess(s, 300, d)
        self.assertEqual(0, d["Carried"])
        self.assertIn("No 2027 entitlement", d["Report"])
        self.assertIn("25 unused", d["Report"])
        self.assertIsNone(self.entitlements()[2026]["fwp_carriedtonextyeardays"])


if __name__ == "__main__":
    unittest.main()
