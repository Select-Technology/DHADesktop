""""Close quote" button on the Quote form (FWPQuoteCloseButton).

Project Operations only offers "Close as won"/"Close as lost" on Active quotes, and the standard Sales
"Close Quote" is disabled for project quotes, so DHA users could not close quotes without God Mode. This adds one
button that closes a Draft or Active quote with a chosen status reason (Draft quotes are activated first).

Step "dev" (against DEV): uploads the two web resources, builds the FWPQuoteCloseButton solution (web resources +
  the Quote table shell), adds the button to Quote's RibbonDiffXml next to the existing unmanaged Enable Fields
  button, imports and publishes.
Step "package" (against DEV): exports FWPQuoteCloseButton managed to DHA.Dataverse/Solutions.
Step "import" (against PROD): imports the managed package and publishes Quote.
Step "verify" (DATAVERSE_URL, default DEV): confirms the button is on the Quote form ribbon.

    python -I provision_quote_close_button.py dev|package|import|verify
"""
import base64, io, json, os, re, sys, time, uuid, zipfile
import importlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
DEV, PROD = "https://dhapd-dev.crm11.dynamics.com", "https://dhapd.crm11.dynamics.com"
SOL, SOL_NAME, VERSION = "FWPQuoteCloseButton", "FWP Quote Close Button", "1.0.0.0"
WEB = os.path.join(HERE, "..", "WebResources")
WEB_RESOURCES = [  # (name, file, webresourcetype: 1 = HTML, 3 = JScript)
    ("fwp_quoteCloseCommand.js", "fwp_quoteCloseCommand.js", 3),
    ("fwp_quoteCloseDialog.html", "fwp_quoteCloseDialog.html", 1),
]
PACKAGE = os.path.join(HERE, "..", "Solutions", f"{SOL}_{VERSION.replace('.', '_')}_managed.zip")
BUTTON_ID = "fwp.quote.CloseQuote.Button"

CUSTOM_ACTION = """<CustomAction Id="fwp.quote.CloseQuote.CustomAction" Location="Mscrm.Form.quote.MainTab.Save.Controls._children" Sequence="39">
            <CommandUIDefinition>
              <Button Id="fwp.quote.CloseQuote.Button" Command="fwp.quote.CloseQuote.Command" LabelText="Close quote" Alt="Close quote" Sequence="39" TemplateAlias="o1" ToolTipTitle="Close quote" ToolTipDescription="Close this quote as Lost, Canceled or another reason. A draft quote is activated first." ModernImage="$webresource:msdyn_/imgs/CustomActions/CloseAsLost.svg" />
            </CommandUIDefinition>
          </CustomAction>"""
COMMAND = """<CommandDefinition Id="fwp.quote.CloseQuote.Command">
            <EnableRules>
              <EnableRule Id="fwp.quote.CloseQuote.EnableRule" />
            </EnableRules>
            <DisplayRules />
            <Actions>
              <JavaScriptFunction FunctionName="FwpQuoteClose.open" Library="$webresource:fwp_quoteCloseCommand.js">
                <CrmParameter Value="PrimaryControl" />
              </JavaScriptFunction>
            </Actions>
          </CommandDefinition>"""
ENABLE_RULE = """<EnableRule Id="fwp.quote.CloseQuote.EnableRule">
              <CustomRule FunctionName="FwpQuoteClose.canClose" Library="$webresource:fwp_quoteCloseCommand.js" Default="false">
                <CrmParameter Value="PrimaryControl" />
              </CustomRule>
            </EnableRule>"""


def env(url):
    os.environ["DATAVERSE_URL"] = url
    import dv
    importlib.reload(dv)
    return dv


def ok(s, d, what):
    print(what, s)
    if s >= 300:
        print(str(d)[:1500]); sys.exit(1)


def one(dv, path):
    s, d, _ = dv.call("GET", path.replace(" ", "%20"))
    ok(s, d, "GET " + path.split("?")[0])
    return d["value"][0] if d["value"] else None


def ensure_solution(dv):
    if one(dv, f"solutions?$select=solutionid&$filter=uniquename eq '{SOL}'"):
        return
    pub = one(dv, "publishers?$select=publisherid&$filter=uniquename eq 'SelectTechnology'")
    s, d, _ = dv.call("POST", "solutions", {"uniquename": SOL, "friendlyname": SOL_NAME, "version": VERSION,
                                            "publisherid@odata.bind": f"/publishers({pub['publisherid']})"})
    ok(s, d, "create solution")


def upsert_web_resources(dv):
    for name, file, wrtype in WEB_RESOURCES:
        with open(os.path.join(WEB, file), "rb") as f:
            content = base64.b64encode(f.read()).decode()
        existing = one(dv, f"webresourceset?$select=webresourceid&$filter=name eq '{name}'")
        if existing:
            s, d, _ = dv.call("PATCH", f"webresourceset({existing['webresourceid']})", {"content": content}, solution=SOL)
        else:
            s, d, _ = dv.call("POST", "webresourceset", {"name": name, "displayname": name, "webresourcetype": wrtype,
                                                         "content": content}, solution=SOL)
        ok(s, d, "web resource " + name)


def add_quote_shell(dv):
    s, d, _ = dv.call("GET", "EntityDefinitions(LogicalName='quote')?$select=MetadataId")
    ok(s, d, "quote metadata")
    s, d, _ = dv.call("POST", "AddSolutionComponent", {"ComponentId": d["MetadataId"], "ComponentType": 1, "SolutionUniqueName": SOL,
                                                       "AddRequiredComponents": False, "DoNotIncludeSubcomponents": True})
    ok(s, d, "add Quote shell")


def export(dv, managed):
    s, d, _ = dv.call("POST", "ExportSolution", {"SolutionName": SOL, "Managed": managed})
    ok(s, d, "export " + ("managed" if managed else "unmanaged"))
    return base64.b64decode(d["ExportSolutionFile"])


def import_zip(dv, data):
    s, d, _ = dv.call("POST", "ImportSolution", {"CustomizationFile": base64.b64encode(data).decode(), "OverwriteUnmanagedCustomizations": True,
                                                 "PublishWorkflows": False, "ImportJobId": str(uuid.uuid4())})
    ok(s, d, "import")


def publish(dv):
    ids = [one(dv, f"webresourceset?$select=webresourceid&$filter=name eq '{n}'")["webresourceid"] for n, _, _ in WEB_RESOURCES]
    xml = ("<importexportxml><entities><entity>quote</entity></entities><webresources>"
           + "".join(f"<webresource>{{{i}}}</webresource>" for i in ids) + "</webresources></importexportxml>")
    s, d, _ = dv.call("POST", "PublishXml", {"ParameterXml": xml})
    ok(s, d, "publish")


def add_button(customizations):
    """Adds the button to Quote's RibbonDiffXml, keeping whatever is already there (dev: Enable Fields)."""
    m = re.search(r"<RibbonDiffXml>.*?</RibbonDiffXml>", customizations, re.S)
    assert m, "Quote RibbonDiffXml not in the export"
    r = m.group(0)
    if BUTTON_ID in r:
        return customizations

    def insert(xml, empty_tag, open_tag, close_tag, item):
        if empty_tag in xml:
            return xml.replace(empty_tag, f"{open_tag}\n          {item}\n        {close_tag}", 1)
        assert close_tag in xml, close_tag
        return xml.replace(close_tag, f"  {item}\n        {close_tag}", 1)

    r = insert(r, "<CustomActions />", "<CustomActions>", "</CustomActions>", CUSTOM_ACTION)
    r = insert(r, "<CommandDefinitions />", "<CommandDefinitions>", "</CommandDefinitions>", COMMAND)
    rules = re.search(r"<RuleDefinitions>.*?</RuleDefinitions>", r, re.S).group(0)
    rules = insert(rules, "<EnableRules />", "<EnableRules>", "</EnableRules>", ENABLE_RULE)
    r = re.sub(r"<RuleDefinitions>.*?</RuleDefinitions>", lambda _: rules, r, count=1, flags=re.S)
    return customizations[:m.start()] + r + customizations[m.end():]


def rezip(data, edit):
    src = zipfile.ZipFile(io.BytesIO(data))
    out = io.BytesIO()
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as dst:
        for item in src.infolist():
            content = src.read(item.filename)
            if item.filename == "customizations.xml":
                content = edit(content.decode("utf-8")).encode("utf-8")
            dst.writestr(item, content)
    return out.getvalue()


def form_ribbon(dv):
    s, d, _ = dv.call("GET", "RetrieveEntityRibbon(EntityName='quote',RibbonLocationFilter=Microsoft.Dynamics.CRM.RibbonLocationFilters'Form')")
    ok(s, d, "ribbon")
    z = zipfile.ZipFile(io.BytesIO(base64.b64decode(d["CompressedEntityXml"])))
    return z.read(z.namelist()[0]).decode("utf-8")


def verify(dv):
    x = form_ribbon(dv)
    for needle in (BUTTON_ID, "fwp.quote.CloseQuote.EnableRule", "msdyn.quote.buttons.closeAsWon", "fwp.form.EnableFields.Button"):
        print(f"  {needle}: {needle in x}")
    if BUTTON_ID not in x:
        sys.exit("Close quote button missing")


def main(step):
    if step == "dev":
        dv = env(DEV)
        ensure_solution(dv)
        upsert_web_resources(dv)
        add_quote_shell(dv)
        before = export(dv, False)
        with open(os.path.join(HERE, "..", "Solutions", f"{SOL}_dev_before.zip"), "wb") as f:
            f.write(before)
        import_zip(dv, rezip(before, add_button))
        publish(dv)
        verify(dv)
    elif step == "package":
        dv = env(DEV)
        data = export(dv, True)
        with open(PACKAGE, "wb") as f:
            f.write(data)
        print("wrote", os.path.normpath(PACKAGE))
    elif step == "import":
        dv = env(PROD)
        with open(PACKAGE, "rb") as f:
            import_zip(dv, f.read())
        publish(dv)
        verify(dv)
    elif step == "verify":
        verify(env(os.environ.get("DATAVERSE_URL", DEV)))
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "")
