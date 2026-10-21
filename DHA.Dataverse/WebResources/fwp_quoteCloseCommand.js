// "Close quote" command for the Quote form
// One button that closes a Draft or Active quote with a chosen status reason (Lost, Canceled, Client Not
// Proceeding, ...). Project Operations only offers "Close as won"/"Close as lost" on Active quotes, and the
// standard Sales "Close Quote" is disabled for project quotes, so DHA users could not close quotes themselves.
// A Draft quote is activated first, because Dataverse only closes Active quotes.
//
// Ribbon (Quote form, FWPQuoteCloseButton solution):
//   Enable rule -> FwpQuoteClose.canClose (PrimaryControl)
//   Action      -> FwpQuoteClose.open (PrimaryControl)
// The reason picker and the close itself live in fwp_quoteCloseDialog.html.

var FwpQuoteClose = {

    // Configuration
    DIALOG: "fwp_quoteCloseDialog.html",
    STATE_DRAFT: 0,
    STATE_ACTIVE: 1,

    canClose: function (formContext) {
        if (!formContext || !formContext.data || !formContext.ui) {
            return false;
        }
        if (formContext.ui.getFormType() === 1) {  // not on a new, unsaved quote
            return false;
        }
        var state = formContext.getAttribute("statecode");
        var value = state ? state.getValue() : null;
        return value === this.STATE_DRAFT || value === this.STATE_ACTIVE;
    },

    open: function (formContext) {
        var self = this;
        var showDialog = function () {
            var id = formContext.data.entity.getId().replace("{", "").replace("}", "");
            var numberAttr = formContext.getAttribute("quotenumber");
            var nameAttr = formContext.getAttribute("name");
            var data = {
                id: id,
                state: formContext.getAttribute("statecode").getValue(),
                number: numberAttr ? numberAttr.getValue() : "",
                name: nameAttr ? nameAttr.getValue() : ""
            };

            Xrm.Navigation.navigateTo(
                { pageType: "webresource", webresourceName: self.DIALOG, data: JSON.stringify(data) },
                { target: 2, position: 1, width: { value: 460, unit: "px" }, height: { value: 430, unit: "px" }, title: "Close quote" }
            ).then(function () {
                // The quote may now be closed (read-only), so reopen it rather than refresh in place
                Xrm.Navigation.openForm({ entityName: "quote", entityId: id });
            }, function (error) {
                Xrm.Navigation.openErrorDialog({ message: error.message });
            });
        };

        // Unsaved changes would be lost once the quote is closed
        if (formContext.data.entity.getIsDirty()) {
            formContext.data.save().then(showDialog, function (error) {
                Xrm.Navigation.openErrorDialog({ message: "Save the quote before closing it. " + (error && error.message ? error.message : "") });
            });
        } else {
            showDialog();
        }
    }
};
