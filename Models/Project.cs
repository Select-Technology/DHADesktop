using System;
using Microsoft.Xrm.Sdk;

namespace DHA.DSTC.WPF.Models
{
    public class Project
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Number { get; set; }
        public string Client { get; set; }
        public bool IsActive { get; set; }

        public Project()
        {
            Id = Guid.Empty;
            IsActive = true;
        }

        public override string ToString()
        {
            return $"{Number} - {Name}";
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
                if (entity.Contains("msdyn_customer"))
                {
                    var customerRef = entity.GetAttributeValue<EntityReference>("msdyn_customer");
                    return customerRef?.Name ?? "";
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        // Convert from Dataverse Entity to Project model
        public static Project FromEntity(Entity entity)
        {
            if (entity == null)
                return null;

            try
            {
                var project = new Project
                {
                    Id = entity.Id,
                    IsActive = true // Default to active
                };

                // Safely get string attributes with fallbacks
                project.Name = SafeGetString(entity, "msdyn_subject", "Unknown");
                project.Number = SafeGetString(entity, "isc_projectnumbernew", "");
                project.Client = SafeGetClientName(entity);

                // Explicitly check and set IsActive based on statuscode
                if (entity.Contains("statuscode"))
                {
                    var statusCode = entity.GetAttributeValue<OptionSetValue>("statuscode");

                    // Explicitly set IsActive to false if statuscode is 192350001 (Completed)
                    project.IsActive = statusCode?.Value != 192350001;

                    // Optional: Additional logging or debugging
                    System.Diagnostics.Debug.WriteLine($"Project {project.Number}: StatusCode = {statusCode?.Value}, IsActive = {project.IsActive}");
                }

                return project;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error converting entity to Project: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return new Project { Name = "Error loading project" };
            }
        }

        // Convert from Project model to Dataverse Entity
        public Entity ToEntity()
        {
            var entity = new Entity("msdyn_project");

            if (Id != Guid.Empty)
                entity.Id = Id;

            entity["msdyn_subject"] = Name;
            entity["isc_projectnumber"] = Number;

            // Only set customer if we have a value
            if (!string.IsNullOrWhiteSpace(Client))
            {
                entity["msdyn_customer"] = Client;
            }

            // Only set state if needed
            if (!IsActive)
            {
                entity["statuscode"] = new OptionSetValue(192350001); // Completed status code
            }

            return entity;
        }
    }
}