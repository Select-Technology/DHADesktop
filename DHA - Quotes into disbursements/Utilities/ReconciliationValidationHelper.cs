using System;
using System.Linq;
using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DHA.DSTC.WPF.Utilities
{
    /// <summary>
    /// Validation helper to prevent modifications to time entries and disbursements 
    /// that have been reconciled in fwp_reconciledvaluedirect
    /// </summary>
    public static class ReconciliationValidationHelper
    {
        /// <summary>
        /// Checks if a time entry can be modified based on reconciliation status
        /// </summary>
        /// <param name="timeEntryId">GUID of the time entry</param>
        /// <param name="connector">Dataverse connector</param>
        /// <returns>ReconciliationValidationResult indicating if modification is allowed</returns>
        public static ReconciliationValidationResult ValidateTimeEntryModification(Guid timeEntryId, DataverseConnector connector)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== RECONCILIATION VALIDATION START ===");
                System.Diagnostics.Debug.WriteLine($"TimeEntry ID: {timeEntryId}");

                if (!connector.Connect())
                {
                    System.Diagnostics.Debug.WriteLine("❌ CONNECTION FAILED - returning Success (allowing edit)");
                    return ReconciliationValidationResult.Success();
                }

                System.Diagnostics.Debug.WriteLine("✅ Connected to Dataverse");

                // FIXED: Query the fwp_timeentry table to get the fwp_reconciledvaluedirect FIELD
                var query = new QueryExpression("fwp_timeentry")
                {
                    ColumnSet = new ColumnSet("fwp_reconciledvaluedirect", "createdon", "fwp_date"),
                    Criteria = new FilterExpression()
                };

                // Use the correct primary key field name for time entry
                query.Criteria.AddCondition("fwp_timeentryid", ConditionOperator.Equal, timeEntryId);

                System.Diagnostics.Debug.WriteLine($"Querying fwp_timeentry table for timeentryid = {timeEntryId}");

                var result = connector._orgService.RetrieveMultiple(query);

                System.Diagnostics.Debug.WriteLine($"Query returned {result.Entities.Count} time entry records");

                if (result.Entities.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("❌ TIME ENTRY NOT FOUND - allowing edit");
                    return ReconciliationValidationResult.Success();
                }

                var timeEntryRecord = result.Entities.First();

                // DEBUG: Show all available fields on the time entry
                System.Diagnostics.Debug.WriteLine($"Available fields on time entry: {string.Join(", ", timeEntryRecord.Attributes.Keys)}");

                // Check the fwp_reconciledvaluedirect FIELD on this time entry
                decimal reconciledValue = 0m;

                if (timeEntryRecord.Contains("fwp_reconciledvaluedirect"))
                {
                    var rawValue = timeEntryRecord["fwp_reconciledvaluedirect"];
                    System.Diagnostics.Debug.WriteLine($"fwp_reconciledvaluedirect field value: {rawValue} (Type: {rawValue?.GetType()})");

                    // Handle different possible data types
                    if (rawValue is Money moneyValue)
                    {
                        reconciledValue = moneyValue.Value;
                        System.Diagnostics.Debug.WriteLine($"Converted Money value: {reconciledValue}");
                    }
                    else if (rawValue is decimal decimalValue)
                    {
                        reconciledValue = decimalValue;
                        System.Diagnostics.Debug.WriteLine($"Used decimal value: {reconciledValue}");
                    }
                    else if (rawValue is int intValue)
                    {
                        reconciledValue = intValue;
                        System.Diagnostics.Debug.WriteLine($"Converted int value: {reconciledValue}");
                    }
                    else if (rawValue != null && decimal.TryParse(rawValue.ToString(), out decimal parsedValue))
                    {
                        reconciledValue = parsedValue;
                        System.Diagnostics.Debug.WriteLine($"Parsed string value: {reconciledValue}");
                    }
                    else if (rawValue == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Field value is null - treating as 0");
                        reconciledValue = 0m;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Unknown data type: {rawValue.GetType()} - treating as 0");
                        reconciledValue = 0m;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ fwp_reconciledvaluedirect field NOT FOUND on time entry record");
                    // Field doesn't exist - treat as not reconciled
                    reconciledValue = 0m;
                }

                System.Diagnostics.Debug.WriteLine($"Final reconciled value: {reconciledValue}");

                // CRITICAL: Block edit if value is non-zero
                if (reconciledValue != 0)
                {
                    System.Diagnostics.Debug.WriteLine($"🔒 BLOCKING EDIT - Reconciled value is {reconciledValue} (non-zero)");

                    var entryDate = timeEntryRecord.GetAttributeValue<DateTime>("fwp_date");
                    var createdOn = timeEntryRecord.GetAttributeValue<DateTime>("createdon");

                    var errorResult = ReconciliationValidationResult.Error($"⚠️ Time Entry Cannot Be Modified\n\n" +
                        $"This time entry has been reconciled and cannot be changed.\n" +
                        $"Entry Date: {entryDate:dddd, dd MMMM yyyy}\n" +
                        $"Reconciled Value: £{reconciledValue:F2}\n" +
                        $"Entry Created: {createdOn:dddd, dd MMMM yyyy 'at' hh:mm tt}\n\n" +
                        $"💡 Contact your supervisor if changes are required.");

                    System.Diagnostics.Debug.WriteLine($"Returning ERROR result: IsValid={errorResult.IsValid}, Level={errorResult.Level}");
                    return errorResult;
                }

                System.Diagnostics.Debug.WriteLine($"✅ ALLOWING EDIT - Reconciled value is {reconciledValue} (zero or null)");
                var successResult = ReconciliationValidationResult.Success();
                System.Diagnostics.Debug.WriteLine($"Returning SUCCESS result: IsValid={successResult.IsValid}, Level={successResult.Level}");
                return successResult;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ EXCEPTION in reconciliation validation: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Exception type: {ex.GetType().Name}");

                // Allow modification when we can't verify due to errors
                var successResult = ReconciliationValidationResult.Success();
                System.Diagnostics.Debug.WriteLine($"Exception - returning SUCCESS: {successResult.IsValid}");
                return successResult;
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine($"=== RECONCILIATION VALIDATION END ===");
            }
        }



        /// <summary>
        /// Checks if a disbursement can be modified based on reconciliation status
        /// </summary>
        /// <param name="disbursementId">GUID of the disbursement</param>
        /// <param name="connector">Dataverse connector</param>
        /// <returns>ReconciliationValidationResult indicating if modification is allowed</returns>
        public static ReconciliationValidationResult ValidateDisbursementModification(Guid disbursementId, DataverseConnector connector)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== DISBURSEMENT RECONCILIATION VALIDATION START ===");
                System.Diagnostics.Debug.WriteLine($"Disbursement ID: {disbursementId}");

                if (!connector.Connect())
                {
                    System.Diagnostics.Debug.WriteLine("❌ CONNECTION FAILED - returning Success (allowing edit)");
                    return ReconciliationValidationResult.Success();
                }

                System.Diagnostics.Debug.WriteLine("✅ Connected to Dataverse");

                // FIXED: Query the fwp_disbursement table to get the fwp_reconciledvaluedirect FIELD
                var query = new QueryExpression("fwp_disbursement")
                {
                    ColumnSet = new ColumnSet("fwp_reconciledvaluedirect", "createdon", "fwp_date"),
                    Criteria = new FilterExpression()
                };

                // Use the correct primary key field name for disbursement
                query.Criteria.AddCondition("fwp_disbursementid", ConditionOperator.Equal, disbursementId);

                System.Diagnostics.Debug.WriteLine($"Querying fwp_disbursement table for disbursementid = {disbursementId}");

                var result = connector._orgService.RetrieveMultiple(query);

                System.Diagnostics.Debug.WriteLine($"Query returned {result.Entities.Count} disbursement records");

                if (result.Entities.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("❌ DISBURSEMENT NOT FOUND - allowing edit");
                    return ReconciliationValidationResult.Success();
                }

                var disbursementRecord = result.Entities.First();

                // DEBUG: Show all available fields on the disbursement
                System.Diagnostics.Debug.WriteLine($"Available fields on disbursement: {string.Join(", ", disbursementRecord.Attributes.Keys)}");

                // Check the fwp_reconciledvaluedirect FIELD on this disbursement
                decimal reconciledValue = 0m;

                if (disbursementRecord.Contains("fwp_reconciledvaluedirect"))
                {
                    var rawValue = disbursementRecord["fwp_reconciledvaluedirect"];
                    System.Diagnostics.Debug.WriteLine($"fwp_reconciledvaluedirect field value: {rawValue} (Type: {rawValue?.GetType()})");

                    // Handle different possible data types
                    if (rawValue is Money moneyValue)
                    {
                        reconciledValue = moneyValue.Value;
                    }
                    else if (rawValue is decimal decimalValue)
                    {
                        reconciledValue = decimalValue;
                    }
                    else if (rawValue is int intValue)
                    {
                        reconciledValue = intValue;
                    }
                    else if (rawValue != null && decimal.TryParse(rawValue.ToString(), out decimal parsedValue))
                    {
                        reconciledValue = parsedValue;
                    }
                    else if (rawValue == null)
                    {
                        reconciledValue = 0m;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ fwp_reconciledvaluedirect field NOT FOUND on disbursement record");
                    reconciledValue = 0m;
                }

                System.Diagnostics.Debug.WriteLine($"Final reconciled value: {reconciledValue}");

                // CRITICAL: Block edit if value is non-zero
                if (reconciledValue != 0)
                {
                    System.Diagnostics.Debug.WriteLine($"🔒 BLOCKING EDIT - Reconciled value is {reconciledValue} (non-zero)");

                    var entryDate = disbursementRecord.GetAttributeValue<DateTime>("fwp_date");
                    var createdOn = disbursementRecord.GetAttributeValue<DateTime>("createdon");

                    return ReconciliationValidationResult.Error($"⚠️ Disbursement Cannot Be Modified\n\n" +
                        $"This disbursement has been reconciled and cannot be changed.\n" +
                        $"Entry Date: {entryDate:dddd, dd MMMM yyyy}\n" +
                        $"Reconciled Value: £{reconciledValue:F2}\n" +
                        $"Entry Created: {createdOn:dddd, dd MMMM yyyy 'at' hh:mm tt}\n\n" +
                        $"💡 Contact your supervisor if changes are required.");
                }

                System.Diagnostics.Debug.WriteLine($"✅ ALLOWING EDIT - Reconciled value is {reconciledValue} (zero)");
                return ReconciliationValidationResult.Success();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ EXCEPTION in disbursement reconciliation validation: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Exception type: {ex.GetType().Name}");

                // Allow modification when we can't verify due to errors
                return ReconciliationValidationResult.Success();
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine($"=== DISBURSEMENT RECONCILIATION VALIDATION END ===");
            }
        }

        /// <summary>
        /// Checks if a time entry can be deleted based on reconciliation status
        /// </summary>
        /// <param name="timeEntryId">GUID of the time entry</param>
        /// <param name="connector">Dataverse connector</param>
        /// <returns>ReconciliationValidationResult indicating if deletion is allowed</returns>
        public static ReconciliationValidationResult ValidateTimeEntryDeletion(Guid timeEntryId, DataverseConnector connector)
        {
            var validationResult = ValidateTimeEntryModification(timeEntryId, connector);

            if (!validationResult.IsValid)
            {
                // Update the message for deletion context
                return ReconciliationValidationResult.Error(validationResult.Message.Replace("Cannot Be Modified", "Cannot Be Deleted"));
            }

            return ReconciliationValidationResult.Success();
        }

        /// <summary>
        /// Checks if a disbursement can be deleted based on reconciliation status
        /// </summary>
        /// <param name="disbursementId">GUID of the disbursement</param>
        /// <param name="connector">Dataverse connector</param>
        /// <returns>ReconciliationValidationResult indicating if deletion is allowed</returns>
        public static ReconciliationValidationResult ValidateDisbursementDeletion(Guid disbursementId, DataverseConnector connector)
        {
            var validationResult = ValidateDisbursementModification(disbursementId, connector);

            if (!validationResult.IsValid)
            {
                // Update the message for deletion context
                return ReconciliationValidationResult.Error(validationResult.Message.Replace("Cannot Be Modified", "Cannot Be Deleted"));
            }

            return ReconciliationValidationResult.Success();
        }

        /// <summary>
        /// Gets reconciliation details for a time entry (for informational purposes)
        /// </summary>
        /// <param name="timeEntryId">GUID of the time entry</param>
        /// <param name="connector">Dataverse connector</param>
        /// <returns>Reconciliation information or null if not reconciled</returns>
        public static ReconciliationInfo GetTimeEntryReconciliationInfo(Guid timeEntryId, DataverseConnector connector)
        {
            try
            {
                if (!connector.Connect())
                {
                    return null;
                }

                var query = new QueryExpression("fwp_reconciledvaluedirect")
                {
                    ColumnSet = new ColumnSet("fwp_reconciledvaluedirectid", "createdon", "createdby", "fwp_amount"),
                    Criteria = new FilterExpression()
                };

                query.Criteria.AddCondition("fwp_timeentry", ConditionOperator.Equal, timeEntryId);

                // Add join to get created by user name
                var userLink = query.AddLink("systemuser", "createdby", "systemuserid", JoinOperator.LeftOuter);
                userLink.EntityAlias = "creator";
                userLink.Columns = new ColumnSet("fullname");

                var result = connector._orgService.RetrieveMultiple(query);

                if (result.Entities.Count > 0)
                {
                    var reconciliationRecord = result.Entities.First();
                    return new ReconciliationInfo
                    {
                        ReconciliationId = reconciliationRecord.Id,
                        ReconciliationDate = reconciliationRecord.GetAttributeValue<DateTime>("createdon"),
                        ReconciledBy = reconciliationRecord.GetAttributeValue<AliasedValue>("creator.fullname")?.Value?.ToString() ?? "Unknown",
                        Amount = reconciliationRecord.GetAttributeValue<Money>("fwp_amount")?.Value ?? 0m
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting time entry reconciliation info: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets reconciliation details for a disbursement (for informational purposes)
        /// </summary>
        /// <param name="disbursementId">GUID of the disbursement</param>
        /// <param name="connector">Dataverse connector</param>
        /// <returns>Reconciliation information or null if not reconciled</returns>
        public static ReconciliationInfo GetDisbursementReconciliationInfo(Guid disbursementId, DataverseConnector connector)
        {
            try
            {
                if (!connector.Connect())
                {
                    return null;
                }

                var query = new QueryExpression("fwp_reconciledvaluedirect")
                {
                    ColumnSet = new ColumnSet("fwp_reconciledvaluedirectid", "createdon", "createdby", "fwp_amount"),
                    Criteria = new FilterExpression()
                };

                query.Criteria.AddCondition("fwp_disbursement", ConditionOperator.Equal, disbursementId);

                // Add join to get created by user name
                var userLink = query.AddLink("systemuser", "createdby", "systemuserid", JoinOperator.LeftOuter);
                userLink.EntityAlias = "creator";
                userLink.Columns = new ColumnSet("fullname");

                var result = connector._orgService.RetrieveMultiple(query);

                if (result.Entities.Count > 0)
                {
                    var reconciliationRecord = result.Entities.First();
                    return new ReconciliationInfo
                    {
                        ReconciliationId = reconciliationRecord.Id,
                        ReconciliationDate = reconciliationRecord.GetAttributeValue<DateTime>("createdon"),
                        ReconciledBy = reconciliationRecord.GetAttributeValue<AliasedValue>("creator.fullname")?.Value?.ToString() ?? "Unknown",
                        Amount = reconciliationRecord.GetAttributeValue<Money>("fwp_amount")?.Value ?? 0m
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting disbursement reconciliation info: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Quick check to see if a time entry is reconciled (for UI indicators)
        /// </summary>
        /// <param name="timeEntryId">GUID of the time entry</param>
        /// <param name="connector">Dataverse connector</param>
        /// <returns>True if reconciled, false otherwise</returns>
        public static bool IsTimeEntryReconciled(Guid timeEntryId, DataverseConnector connector)
        {
            var validation = ValidateTimeEntryModification(timeEntryId, connector);
            return !validation.IsValid;
        }

        /// <summary>
        /// Quick check to see if a disbursement is reconciled (for UI indicators)
        /// </summary>
        /// <param name="disbursementId">GUID of the disbursement</param>
        /// <param name="connector">Dataverse connector</param>
        /// <returns>True if reconciled, false otherwise</returns>
        public static bool IsDisbursementReconciled(Guid disbursementId, DataverseConnector connector)
        {
            var validation = ValidateDisbursementModification(disbursementId, connector);
            return !validation.IsValid;
        }
    }

    /// <summary>
    /// Represents the result of a reconciliation validation check
    /// Using a different name to avoid conflicts with existing ValidationResult
    /// </summary>
    public class ReconciliationValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
        public ReconciliationValidationLevel Level { get; set; }

        public static ReconciliationValidationResult Success()
        {
            return new ReconciliationValidationResult { IsValid = true, Level = ReconciliationValidationLevel.Success };
        }

        public static ReconciliationValidationResult Error(string message)
        {
            return new ReconciliationValidationResult { IsValid = false, Message = message, Level = ReconciliationValidationLevel.Error };
        }

        public static ReconciliationValidationResult Warning(string message)
        {
            return new ReconciliationValidationResult { IsValid = true, Message = message, Level = ReconciliationValidationLevel.Warning };
        }
    }

    /// <summary>
    /// Validation levels for reconciliation checks
    /// </summary>
    public enum ReconciliationValidationLevel
    {
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Information about a reconciliation record
    /// </summary>
    public class ReconciliationInfo
    {
        public Guid ReconciliationId { get; set; }
        public DateTime ReconciliationDate { get; set; }
        public string ReconciledBy { get; set; }
        public decimal Amount { get; set; }
    }
}