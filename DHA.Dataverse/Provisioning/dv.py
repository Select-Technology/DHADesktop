import json, subprocess, urllib.request, urllib.error, shutil
import os
# Target environment - defaults to DEV. Set DATAVERSE_URL to deploy elsewhere.
ORG = os.environ.get("DATAVERSE_URL", "https://dhapd-dev.crm11.dynamics.com")
_tok = None
def tok():
    global _tok
    if not _tok:
        az = shutil.which("az") or shutil.which("az.cmd")
        _tok = subprocess.check_output([az, "account", "get-access-token", "--resource", ORG, "--query", "accessToken", "-o", "tsv"], text=True, shell=False).strip()
    return _tok
def call(method, path, body=None, solution=None, extra=None):
    h = {"Authorization": "Bearer " + tok(), "Accept": "application/json", "OData-MaxVersion": "4.0", "OData-Version": "4.0",
         "Content-Type": "application/json; charset=utf-8", "Prefer": 'odata.include-annotations="*"'}
    if solution: h["MSCRM.SolutionUniqueName"] = solution
    if extra: h.update(extra)
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(ORG + "/api/data/v9.2/" + path, data=data, method=method, headers=h)
    try:
        with urllib.request.urlopen(req) as r:
            txt = r.read().decode("utf-8")
            return r.status, (json.loads(txt) if txt else None), dict(r.headers)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8"), {}
