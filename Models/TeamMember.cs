using System;
using Microsoft.Xrm.Sdk;

namespace DHA.DSTC.WPF.Models
{
    public class TeamMember
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public bool IsActive { get; set; }

        public string FullName => $"{FirstName} {LastName}";

        public TeamMember()
        {
            Id = Guid.Empty;
            IsActive = true;
        }

        public override string ToString()
        {
            return FullName;
        }

        // Convert from Dataverse Entity to TeamMember model
        public static TeamMember FromEntity(Entity entity)
        {
            if (entity == null)
                return null;

            return new TeamMember
            {
                Id = entity.Id,
                FirstName = entity.GetAttributeValue<string>("firstname"),
                LastName = entity.GetAttributeValue<string>("lastname"),
                Email = entity.GetAttributeValue<string>("internalemailaddress"),
                IsActive = entity.GetAttributeValue<bool>("isdisabled") == false
            };
        }

        // Convert from TeamMember model to Dataverse Entity
        public Entity ToEntity()
        {
            var entity = new Entity("systemuser");

            if (Id != Guid.Empty)
                entity.Id = Id;

            entity["firstname"] = FirstName;
            entity["lastname"] = LastName;
            entity["internalemailaddress"] = Email;

            // Only set state if needed
            if (!IsActive)
            {
                entity["isdisabled"] = true; // Inactive
            }

            return entity;
        }
    }
}