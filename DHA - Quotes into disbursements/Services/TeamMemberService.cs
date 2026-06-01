using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DHA.DSTC.WPF.Services
{
    public class TeamMemberService
    {
        private readonly DataverseConnector _connector;

        public TeamMemberService(DataverseConnector connector)
        {
            _connector = connector;
        }

        public List<TeamMember> GetTeamMembers(bool activeOnly = true)
        {
            _connector.Connect();

            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid", "fullname", "firstname", "lastname", "internalemailaddress", "businessunitid"),
                Orders = {
                    new OrderExpression("fullname", OrderType.Ascending)
                }
            };

            if (activeOnly)
            {
                query.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
            }

            // Add linked entity query to get business unit name
            var businessUnitLink = query.AddLink("businessunit", "businessunitid", "businessunitid");
            businessUnitLink.EntityAlias = "businessunitid";
            businessUnitLink.Columns = new ColumnSet("name");

            var entities = _connector._orgService.RetrieveMultiple(query).Entities.ToList();
            return entities.Select(TeamMember.FromEntity).ToList();
        }

        public TeamMember GetTeamMember(Guid id)
        {
            _connector.Connect();

            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid", "fullname", "internalemailaddress", "businessunitid"),
                Criteria = new FilterExpression()
            };

            query.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, id);

            // Add linked entity query to get business unit name
            var businessUnitLink = query.AddLink("businessunit", "businessunitid", "businessunitid");
            businessUnitLink.EntityAlias = "businessunitid";
            businessUnitLink.Columns = new ColumnSet("name");

            var result = _connector._orgService.RetrieveMultiple(query).Entities;

            if (result.Count > 0)
            {
                return TeamMember.FromEntity(result[0]);
            }

            return null;
        }

        public List<TeamMember> SearchTeamMembers(string searchTerm, bool activeOnly = true)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return GetTeamMembers(activeOnly);
            }

            _connector.Connect();

            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid", "fullname", "internalemailaddress", "businessunitid"),
                Criteria = new FilterExpression(LogicalOperator.And),
                Orders = {
                    new OrderExpression("fullname", OrderType.Ascending)
                }
            };

            // Add search conditions
            var searchGroup = new FilterExpression(LogicalOperator.Or);
            searchGroup.AddCondition("fullname", ConditionOperator.Like, $"%{searchTerm}%");
            searchGroup.AddCondition("internalemailaddress", ConditionOperator.Like, $"%{searchTerm}%");

            query.Criteria.AddFilter(searchGroup);

            if (activeOnly)
            {
                query.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
            }

            // Add linked entity query to get business unit name
            var businessUnitLink = query.AddLink("businessunit", "businessunitid", "businessunitid");
            businessUnitLink.EntityAlias = "businessunitid";
            businessUnitLink.Columns = new ColumnSet("name");

            var entities = _connector._orgService.RetrieveMultiple(query).Entities.ToList();
            return entities.Select(TeamMember.FromEntity).ToList();
        }
    }
}