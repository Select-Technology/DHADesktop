"""DHA-16 - Internal source is mandatory on a quote.

Card: "a quote cannot be saved or submitted without one". Built in dev in solution FWPQuoteInternalSourceMandatory
(18 Sep 2026) by making isc_internalsource Business Required and adding it to the quote forms.

The card leaves two decisions open (required at creation or at submission; what happens to existing quotes) -
the tests report the current state for both.
"""
import unittest

from dvtest import DataverseTestCase, call, get, rows, error_text
import dv  # noqa: E402 - path set up by dvtest


class Dha16InternalSource(DataverseTestCase):

    def test_internal_source_is_business_required(self):
        a = get("EntityDefinitions(LogicalName='quote')/Attributes(LogicalName='isc_internalsource')?$select=RequiredLevel")
        self.assertEqual("ApplicationRequired", a["RequiredLevel"]["Value"])

    def test_every_active_quote_main_form_has_internal_source(self):
        forms = rows("systemforms?$select=name,formxml&$filter=objecttypecode eq 'quote' and type eq 2 and formactivationstate eq 1")
        self.assertGreater(len(forms), 0)
        for f in forms:
            with self.subTest(form=f["name"]):
                self.assertTrue('datafieldname="isc_internalsource"' in f["formxml"],
                                f"Internal Source is not on the '{f['name']}' form - a Business Required field that "
                                f"isn't on a form can't be filled in there")

    def test_internal_source_is_editable_on_the_forms(self):
        for f in rows("systemforms?$select=name,formxml&$filter=objecttypecode eq 'quote' and type eq 2 and formactivationstate eq 1"):
            x = f["formxml"]
            i = x.find('datafieldname="isc_internalsource"')
            if i < 0:
                continue
            control = x[x.rfind("<control", 0, i):x.find(">", i)]
            with self.subTest(form=f["name"]):
                self.assertNotIn('disabled="true"', control, "required but locked - nobody could save the quote")

    def test_quote_without_internal_source_cannot_be_saved_outside_the_form(self):
        """Business Required is only enforced by Dynamics forms. Anything else - the API, flows, imports,
        the desktop app - can still save a quote with no internal source unless a plug-in or similar blocks it."""
        # A customer is genuinely required server-side, so give one - the ONLY thing missing is the internal source
        account = rows("accounts?$select=accountid&$top=1&$filter=statecode eq 0")[0]["accountid"]
        s, d, _ = dv.call("POST", "quotes", {"name": "ZZTEST DHA-16 no internal source",
                                             "customerid_account@odata.bind": f"/accounts({account})"},
                          extra={"Prefer": "return=representation"})
        if s < 300:
            call("DELETE", f"quotes({d['quoteid']})")
        self.assertGreaterEqual(s, 400, "a quote with no internal source was saved through the API")

    def test_report_existing_quotes_without_internal_source(self):
        total = len(rows("quotes?$select=quoteid&$filter=statecode ne 3"))
        missing = len(rows("quotes?$select=quoteid&$filter=statecode ne 3 and _isc_internalsource_value eq null"))
        self.skipTest(f"INFO: {missing} of {total} open/active quotes in dev have no internal source "
                      f"(card question: what happens to existing quotes?)")


if __name__ == "__main__":
    unittest.main()
