using System;
using Microsoft.Xrm.Sdk;

namespace DHA.DSTC.WPF.Models
{
    public class Quote
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string QuoteNumber { get; set; }
        public string Client { get; set; }
        public bool IsActive { get; set; }
        public int StatusCode { get; set; }

        public Quote()
        {
            Id = Guid.Empty;
            IsActive = true;
        }

        public override string ToString()
        {
            return $"{QuoteNumber} - {Name}";
        }

        // Safe string retrieval method
        private static string SafeGetString(Entity entity, string attributeName, string defaultValue = "")
        {
            if (entity == null || string.IsNullOrEmpty(attributeName))
                return defaultValue;

            try
            {
                // Check if attribute exists
                if (!entity.Contains(attributeName))
                    return defaultValue;

                // Handle different possible types
                var value = entity[attributeName];

                // If it's a string, return it
                if (value is string stringValue)
                    return stringValue;

                // If it's an EntityReference, try to get its name
                if (value is EntityReference entityRef)
                    return entityRef.Name ?? defaultValue;

                // Convert to string as fallback
                return value?.ToString() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        // Special method to safely get client name
        private static string SafeGetClientName(Entity entity)
        {
            if (entity == null)
                return "";

            try
            {
                // Check for customer field which might be an EntityReference
                if (entity.Contains("customerid"))
                {
                    var customerRef = entity.GetAttributeValue<EntityReference>("customerid");
                    return customerRef?.Name ?? "";
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        // Convert from Dataverse Entity to Quote model
        public static Quote FromEntity(Entity entity)
        {
            if (entity == null)
                return null;

            try
            {
                var quote = new Quote
                {
                    Id = entity.Id,
                    IsActive = true // Default to active
                };

                // Safely get string attributes with fallbacks
                quote.Name = SafeGetString(entity, "name", "Unknown");
                quote.QuoteNumber = SafeGetString(entity, "quotenumber", "");
                quote.Client = SafeGetClientName(entity);

                // Check status code to determine if active
                if (entity.Contains("statuscode"))
                {
                    var statusCode = entity.GetAttributeValue<OptionSetValue>("statuscode");
                    quote.StatusCode = statusCode?.Value ?? 0;

                    // Set IsActive based on status code (assuming closed statuses are inactive)
                    // You may need to adjust these status codes based on your specific configuration
                    quote.IsActive = statusCode?.Value != 2 && statusCode?.Value != 3; // 2=Won, 3=Lost typically
                }

                return quote;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error converting entity to Quote: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return new Quote { Name = "Error loading quote" };
            }
        }

        // Convert from Quote model to Dataverse Entity
        public Entity ToEntity()
        {
            var entity = new Entity("quote");

            if (Id != Guid.Empty)
                entity.Id = Id;

            entity["name"] = Name;
            entity["quotenumber"] = QuoteNumber;

            // Only set customer if we have a value
            if (!string.IsNullOrWhiteSpace(Client))
            {
                // Note: This would need to be an EntityReference in practice
                // entity["customerid"] = new EntityReference("account", customerId);
            }

            return entity;
        }
    }
}