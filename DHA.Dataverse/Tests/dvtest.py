"""Shared helpers for the DHA Dataverse integration tests.

These tests run against DEV only and create/modify real records there. Every test cleans up after itself.
Run everything with:  python -I run_tests.py
Requires `az login` as stsadmin (System Administrator in dev).
"""
import json
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Provisioning"))
import dv  # noqa: E402

if "dhapd-dev" not in dv.ORG:
    sys.exit(f"Refusing to run tests against {dv.ORG} - these tests create and change data. Dev only.")

FMT = "OData.Community.Display.V1.FormattedValue"

# Dev users with known roles / patterns (see docs/DHA-9-leave-plan.md and the wiki security pages)
JESSICA = "e1890edf-e060-ed11-9562-002248428ff5"     # Project Manager role, standard 7.5h Mon-Fri, not finance
HARRIET = "e666f5d2-e060-ed11-9562-002248428ff5"     # part-time in dev: Mon and Fri off, Tue-Thu 7.5h
ALICE = "ab66fac0-75a0-f111-b8de-002248423048"       # colleague config with times but no expected hours
TEST_PROJECT = "12a3e411-5414-f011-9989-000d3ad5d75f"  # "Test Project for Time & Billing"

# Leave request status option values
PENDING, APPROVED, REJECTED, CANCEL_REQUESTED, CANCELLED = 800470000, 800470001, 800470002, 800470003, 800470004

_me = None


def me():
    global _me
    if _me is None:
        _me = dv.call("GET", "WhoAmI")[1]["UserId"]
    return _me


def as_user(user_id):
    return {"MSCRMCallerID": user_id} if user_id else None


def call(method, path, body=None, user=None):
    s, d, _ = dv.call(method, path.replace(" ", "%20"), body, extra=as_user(user))
    return s, d


def get(path, user=None):
    s, d = call("GET", path, user=user)
    if s >= 300:
        raise AssertionError(f"GET {path} -> {s}: {error_text(d)}")
    return d


def rows(path, user=None):
    return get(path, user)["value"]


def error_text(d):
    if isinstance(d, str):
        try:
            return json.loads(d)["error"]["message"]
        except Exception:
            return d[:400]
    return str(d)


def entity_set(logical):
    return get(f"EntityDefinitions(LogicalName='{logical}')?$select=EntitySetName")["EntitySetName"]


def action(name, body, user=None):
    """Calls an unbound action/Custom API. Returns (status, outputs-or-error-text)."""
    s, d = call("POST", name, body, user)
    if s >= 300:
        return s, error_text(d)
    return s, {k: v for k, v in (d or {}).items() if not k.startswith("@")}


def leave_entries(source_key, include_inactive=True):
    flt = f"fwp_leavesourcekey eq '{source_key}'" + ("" if include_inactive else " and statecode eq 0")
    return rows(f"fwp_timeentries?$select=fwp_date,fwp_decimalhours,fwp_durationhours,fwp_minutes,fwp_notes,fwp_category,"
                f"fwp_classification,statecode,_fwp_project_value,_fwp_teammember_value,_fwp_leaverequest_value"
                f"&$filter={flt}&$orderby=fwp_date")


def active(entries):
    return [e for e in entries if e["statecode"] == 0]


def day_hours(entries):
    """{'2027-03-01': 7.5, ...} for ACTIVE entries."""
    return {e["fwp_date"][:10]: e["fwp_decimalhours"] for e in active(entries)}


def delete_time_entries(source_key):
    """Test clean-up only: removes test time entries (production code never deletes - DHA-10)."""
    for e in leave_entries(source_key):
        call("DELETE", f"fwp_timeentries({e['fwp_timeentryid']})")


class DataverseTestCase(unittest.TestCase):
    maxDiff = None

    def assertActionOk(self, result, msg=None):
        status, body = result
        self.assertLess(status, 300, msg or f"expected success, got {status}: {body}")
        return body

    def assertActionFails(self, result, contains=None):
        status, body = result
        self.assertGreaterEqual(status, 400, f"expected failure, got {status}: {body}")
        if contains:
            self.assertIn(contains.lower(), str(body).lower())
        return body
