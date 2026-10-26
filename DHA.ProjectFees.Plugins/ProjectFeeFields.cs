namespace DHA.ProjectFees.Plugins
{
    /// <summary>
    /// Fee extensions (September 2026 fee adjustment): Finance records extra agreed fee as fwp_feeextension rows.
    /// The project's DHA Fee (isc_projectamount) is then original fee + extensions, and the Project Fee Summary card
    /// shows fwp_originalprojectfee, the extensions and the total.
    /// </summary>
    internal static class ProjectFeeFields
    {
        public const string Project = "msdyn_project";
        public const string DhaFee = "isc_projectamount";
        public const string OriginalFee = "fwp_originalprojectfee";
        public const string ExtensionsTotal = "isc_feeextension";
        public const string LastExtensionDate = "fwp_lastfeeextensiondate";
        public const string PreviousExtensionDate = "fwp_previousfeeextensiondate";

        public const string FeeExtension = "fwp_feeextension";
        public const string ExtensionProject = "fwp_project";
        public const string ExtensionAmount = "fwp_amount";
    }
}
