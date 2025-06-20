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
                project.Name = entity.Contains("msdyn_subject") ? entity.GetAttributeValue<string>("msdyn_subject") : "Unknown";
                project.Number = entity.Contains("isc_projectnumbernew") ? entity.GetAttributeValue<string>("isc_projectnumbernew") : "";
                project.Client = entity.Contains("msydyn_customer") ? entity.GetAttributeValue<string>("msdyn_customer") : "";

                // Try to get state code
                try
                {
                    if (entity.Contains("statecode"))
                    {
                        var stateCode = entity.GetAttributeValue<OptionSetValue>("statecode");
                        project.IsActive = stateCode?.Value == 0; // 0 = Active, 1 = Inactive
                    }
                }
                catch
                {
                    // Default to active if we can't determine state
                    project.IsActive = true;
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
            entity["msdyn_customer"] = Client;

            // Only set state if needed
            if (!IsActive)
            {
                entity["isdisabled"] = true; // Inactive
            }

            return entity;
        }
    }
}