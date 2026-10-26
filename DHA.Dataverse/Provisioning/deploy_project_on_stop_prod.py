"""DHA-7/8: deploys FWPProjectOnStop to PROD without overwriting prod's form layouts (dev and prod forms had drifted).

Step "prepare" (against DEV):
  - "Project - DHA1" and Time Entry "Information": dev form := PROD's current form + only our change
    (On Stop in the header / the on-stop warning script), with DHA1's display-role ids mapped prod -> dev by role name.
  - Project "Information" is removed from the solution: in prod it has unmanaged edits on top, so a managed
    form layer would never show. It is edited directly in prod instead (step "prod-form").
  - Exports FWPProjectOnStop managed to DHA.Dataverse/Solutions.
Step "import" (against PROD): imports the managed solution.
Step "prod-form" (against PROD): adds On Stop to the header of prod's Project "Information" form (the form Finance uses).
Step "members" (against PROD): Read profile <- root business unit team (everyone); Finance profile <- DHA-4 holders.

    python -I deploy_project_on_stop_prod.py prepare|import|prod-form|members
"""
import base64, json, os, re, sys, time, uuid
import importlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
DEV, PROD = "https://dhapd-dev.crm11.dynamics.com", "https://dhapd.crm11.dynamics.com"
SOL = "FWPProjectOnStop"
DHA1, TE_FORM, INFO = "b274aa3d-43af-f011-bbd3-6045bdf1c876", "c682745b-4024-4f1b-bf66-75dadd8300b8", "105ef076-a72a-4382-bde5-35e3c3d9db40"
HEADER_CELL = "{f05bfa67-914b-408c-b815-278a47a3d38d}"
WR = "fwp_projectOnStopWarning"
PACKAGE = os.path.join(HERE, "..", "Solutions", "FWPProjectOnStop_1_0_1_0_managed.zip")
FINANCE_PROFILE, READ_PROFILE = "DHA - Project On Stop: Finance", "DHA - Project On Stop: Read"


def env(url):
    os.environ["DATAVERSE_URL"] = url
    import dv
    importlib.reload(dv)
    return dv


def ok(s, d, what):
    print(what, s)
    if s >= 300:
        print(str(d)[:1500]); sys.exit(1)


def on_stop_cell(cell_id):
    return (f'<cell id="{cell_id}" locklevel="0" colspan="1" rowspan="1" showlabel="true">'
            '<labels><label description="On Stop" languagecode="1033" /></labels>'
            '<control id="header_fwp_onstop" classid="{67FAC785-CD58-4F9F-ABB3-4B7DDC6ED5ED}" datafieldname="fwp_onstop" disabled="false" /></cell>')


def add_on_stop_to_header(x, cell_id=None):
    if 'datafieldname="fwp_onstop"' in x:
        return x
    header = re.search(r"<header.*?</header>", x, re.S)
    if cell_id:
        m = re.search(r'<cell id="' + re.escape(cell_id) + r'"[^>]*>.*?</cell>', header.group(0), re.S)
    else:  # first empty, label-less cell in the header
        m = re.search(r'<cell [^>]*showlabel="false"[^>]*>\s*<labels>\s*<label description="" languagecode="1033" />\s*</labels>\s*</cell>',
                      header.group(0), re.S)
    assert m, "no free header cell"
    cid = re.search(r'id="(\{[^}]+\})"', m.group(0)).group(1)
    new_header = header.group(0)[:m.start()] + on_stop_cell(cid) + header.group(0)[m.end():]
    return x[:header.start()] + new_header + x[header.end():]


def add_warning_script(x):
    if WR in x:
        return x
    handler = lambda fn: (f'<Handler functionName="{fn}" libraryName="{WR}" handlerUniqueId="{{{uuid.uuid4()}}}" '
                          'enabled="true" parameters="" passExecutionContext="true" />')
    x = x.replace("</formLibraries>", f'<Library name="{WR}" libraryUniqueId="{{{uuid.uuid4()}}}" /></formLibraries>', 1)
    x = re.sub(r'(<event name="onload"[^>]*><Handlers>.*?)(</Handlers>)', lambda m: m.group(1) + handler("ProjectOnStopWarning.onLoad") + m.group(2),
               x, count=1, flags=re.S)
    assert "ProjectOnStopWarning.onLoad" in x
    return x.replace("</events>", '<event name="onchange" application="false" active="false" attribute="fwp_project"><Handlers>'
                     + handler("ProjectOnStopWarning.onProjectChange") + "</Handlers></event></events>", 1)


def prepare():
    prod = env(PROD)
    prod_forms = {f: prod.call("GET", f"systemforms({f})?$select=formxml")[1]["formxml"] for f in (DHA1, TE_FORM)}
    prod_roles = {r: prod.call("GET", f"roles({r})?$select=name")[1]["name"] for r in re.findall(r'<Role Id="\{([^}]+)\}"', prod_forms[DHA1])}

    dev = env(DEV)
    root = dev.call("GET", "businessunits?$select=businessunitid&$filter=_parentbusinessunitid_value%20eq%20null")[1]["value"][0]["businessunitid"]
    x = prod_forms[DHA1]
    for pid, name in prod_roles.items():
        dev_role = dev.call("GET", f"roles?$select=roleid&$filter=name%20eq%20'{name.replace(' ', '%20')}'%20and%20_businessunitid_value%20eq%20{root}")[1]["value"]
        assert dev_role, f"no dev role named {name}"
        x = re.sub(r'<Role Id="\{' + pid + r'\}"', f'<Role Id="{{{dev_role[0]["roleid"].upper()}}}"', x, flags=re.I)
        print(f"  DHA1 role {name}: prod {pid} -> dev {dev_role[0]['roleid']}")
    ok(*dev.call("PATCH", f"systemforms({DHA1})", {"formxml": add_on_stop_to_header(x, HEADER_CELL)})[:2], "dev Project - DHA1 := prod + On Stop")
    ok(*dev.call("PATCH", f"systemforms({TE_FORM})", {"formxml": add_warning_script(prod_forms[TE_FORM])})[:2], "dev Time Entry Information := prod + warning script")

    sol = dev.call("GET", f"solutions?$select=solutionid&$filter=uniquename%20eq%20'{SOL}'")[1]["value"][0]["solutionid"]
    comp = dev.call("GET", f"solutioncomponents?$select=solutioncomponentid&$filter=_solutionid_value%20eq%20{sol}%20and%20objectid%20eq%20{INFO}")[1]["value"]
    if comp:
        ok(*dev.call("POST", "RemoveSolutionComponent", {
            "SolutionComponent": {"@odata.type": "Microsoft.Dynamics.CRM.solutioncomponent", "solutioncomponentid": INFO},  # the action reads this as the component's own (form) id
            "ComponentType": 60, "SolutionUniqueName": SOL})[:2],
           "removed Project Information form from the solution")
    ok(*dev.call("PATCH", f"solutions({sol})", {"version": "1.0.1.0"})[:2], "version 1.0.1.0")
    ok(*dev.call("POST", "PublishXml", {"ParameterXml": "<importexportxml><entities><entity>msdyn_project</entity><entity>fwp_timeentry</entity></entities></importexportxml>"})[:2], "publish")
    s, d, _ = dev.call("POST", "ExportSolution", {"SolutionName": SOL, "Managed": True})
    ok(s, d if s >= 300 else "", "export")
    open(PACKAGE, "wb").write(base64.b64decode(d["ExportSolutionFile"]))
    print("  ->", PACKAGE)


def do_import():
    prod = env(PROD)
    data = base64.b64encode(open(PACKAGE, "rb").read()).decode()
    s, d, _ = prod.call("POST", "ImportSolutionAsync", {"CustomizationFile": data, "OverwriteUnmanagedCustomizations": False, "PublishWorkflows": False,
                                                       "ConvertToManaged": False, "SkipProductUpdateDependencies": False, "HoldingSolution": False})
    ok(s, d if s >= 300 else "", "import requested")
    op = d["AsyncOperationId"]
    for _ in range(90):
        a = prod.call("GET", f"asyncoperations({op})?$select=statecode,statuscode,message")[1]
        if a["statecode"] == 3:
            break
        time.sleep(10)
    print("import finished, status", a["statuscode"], "(30 = succeeded)", (a.get("message") or "")[:1000])
    if a["statuscode"] != 30:
        sys.exit(1)


def prod_form():
    prod = env(PROD)
    x = prod.call("GET", f"systemforms({INFO})?$select=formxml")[1]["formxml"]
    if 'datafieldname="fwp_onstop"' in x:
        print("Project Information already has On Stop"); return
    ok(*prod.call("PATCH", f"systemforms({INFO})", {"formxml": add_on_stop_to_header(x)})[:2], "prod Project Information: On Stop added to header")
    ok(*prod.call("POST", "PublishXml", {"ParameterXml": "<importexportxml><entities><entity>msdyn_project</entity></entities></importexportxml>"})[:2], "publish")


def members():
    prod = env(PROD)
    q = lambda p: prod.call("GET", p.replace(" ", "%20"))[1]["value"]
    profile = {n: q(f"fieldsecurityprofiles?$select=fieldsecurityprofileid&$filter=name eq '{n}'")[0]["fieldsecurityprofileid"] for n in (FINANCE_PROFILE, READ_PROFILE)}
    root = q("businessunits?$select=businessunitid&$filter=_parentbusinessunitid_value eq null")[0]["businessunitid"]
    team = q(f"teams?$select=teamid,name&$filter=isdefault eq true and _businessunitid_value eq {root}")[0]
    s, d, _ = prod.call("POST", f"fieldsecurityprofiles({profile[READ_PROFILE]})/teamprofiles_association/$ref",
                        {"@odata.id": f"{prod.ORG}/api/data/v9.2/teams({team['teamid']})"})
    print(f"Read profile <- team {team['name']} (everyone):", s)
    finance = q("systemusers?$select=fullname,systemuserid&$filter=isdisabled eq false and systemuserroles_association/any(r:r/name eq 'DHA-4: Finance User')")
    for u in finance:
        if u["fullname"] in ("Select Support", "Dynamics Automation"):
            continue  # service accounts - add only if they genuinely need to set the stop
        s, d, _ = prod.call("POST", f"fieldsecurityprofiles({profile[FINANCE_PROFILE]})/systemuserprofiles_association/$ref",
                            {"@odata.id": f"{prod.ORG}/api/data/v9.2/systemusers({u['systemuserid']})"})
        print(f"Finance profile <- {u['fullname']}:", s)


{"prepare": prepare, "import": do_import, "prod-form": prod_form, "members": members}[sys.argv[1]]()
