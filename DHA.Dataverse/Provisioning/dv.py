import json, subprocess, sys, urllib.request, urllib.error, shutil
import os
# Target environment - defaults to DEV. Set DATAVERSE_URL to deploy elsewhere.
ORG = os.environ.get("DATAVERSE_URL", "https://dhapd-dev.crm11.dynamics.com")
DHA_TENANT = "28f0e92f-184e-4166-84cd-af41a0b93f83"
_tok = None
# DHA scripts keep their own Azure CLI sign-in, separate from the default one, so signing in for other clients
# can't log the DHA account out. One-off (Windows):
#   set AZURE_CONFIG_DIR=%USERPROFILE%\.azure-dha
#   az login --tenant 28f0e92f-184e-4166-84cd-af41a0b93f83      (as stsadmin@dhaplanning.co.uk)
AZURE_CONFIG_DIR = os.environ.get("DHA_AZURE_CONFIG_DIR", os.path.join(os.path.expanduser("~"), ".azure-dha"))


def az_token(resource):
    """Access token for `resource` from the DHA sign-in (DHA tenant only)."""
    az = shutil.which("az") or shutil.which("az.cmd")
    cmd = [az, "account", "get-access-token", "--resource", resource, "--tenant", DHA_TENANT, "--query", "accessToken", "-o", "tsv"]
    # The dedicated DHA folder first, then the normal sign-in - always pinned to the DHA tenant, so never another client's
    for env in (dict(os.environ, AZURE_CONFIG_DIR=AZURE_CONFIG_DIR), dict(os.environ)):
        try:
            return subprocess.check_output(cmd, text=True, env=env, stderr=subprocess.PIPE).strip()
        except subprocess.CalledProcessError as e:
            err = e
    if True:
        e = err
        sys.exit(f"No DHA sign-in. Run once:\n  set AZURE_CONFIG_DIR={AZURE_CONFIG_DIR}\n  az login --tenant {DHA_TENANT}\n"
                 f"(as stsadmin@dhaplanning.co.uk)\n\n{e.stderr.strip()[:300]}")


def tok(org=None):
    global _tok
    if org and org != ORG:
        _other_toks.setdefault(org, az_token(org))
        return _other_toks[org]
    if not _tok:
        _tok = az_token(ORG)
    return _tok


_other_toks = {}


def call(method, path, body=None, solution=None, extra=None, org=None):
    """org: another environment's URL for this one call (default ORG)."""
    h = {"Authorization": "Bearer " + tok(org), "Accept": "application/json", "OData-MaxVersion": "4.0", "OData-Version": "4.0",
         "Content-Type": "application/json; charset=utf-8", "Prefer": 'odata.include-annotations="*"'}
    if solution: h["MSCRM.SolutionUniqueName"] = solution
    if extra: h.update(extra)
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request((org or ORG) + "/api/data/v9.2/" + path, data=data, method=method, headers=h)
    try:
        with urllib.request.urlopen(req) as r:
            txt = r.read().decode("utf-8")
            return r.status, (json.loads(txt) if txt else None), dict(r.headers)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8"), {}
