using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace DHA.DSTC.WPF.Services
{
    public class QuoteService
    {
        private readonly DataverseConnector _connector;
        private readonly string _entityName = "quote";

        public QuoteService(DataverseConnector connector)
        {
            _connector = connector;
        }

        public List<Quote> GetQuotes()
        {
            try
            {
                // Ensure connection before attempting to retrieve data
                if (!_connector.Connect())
                {
                    MessageBox.Show("Failed to connect to Dataverse",
                        "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return new List<Quote>();
                }

                // Use specific columns instead of retrieving all
                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet(
                        "name",
                        "quotenumber",
                        "customerid",
                        "statuscode"
                    ),
                    Orders = {
                        new OrderExpression("name", OrderType.Ascending)
                    }
                };

                // Filter for active quotes only (exclude Won/Lost/Closed)
                var statusFilter = new FilterExpression(LogicalOperator.And);
                statusFilter.AddCondition("statuscode", ConditionOperator.NotIn, new object[] { 2, 3, 4 }); // Exclude Won, Lost, Closed
                query.Criteria = statusFilter;

                // Increase page size to get more quotes initially
                query.PageInfo = new PagingInfo
                {
                    Count = 5000,
                    PageNumber = 1
                };

                var result = _connector._orgService.RetrieveMultiple(query);

                if (result?.Entities == null)
                {
                    MessageBox.Show("No data returned from Dataverse",
                        "Data Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return new List<Quote>();
                }

                var entities = result.Entities.ToList();
                var quotes = entities
                    .Select(Quote.FromEntity)
                    .Where(q => q != null && q.IsActive)
                    .ToList();

                // Debug logging
                System.Diagnostics.Debug.WriteLine($"Retrieved {entities.Count} total entities, {quotes.Count} active quotes");

                return quotes;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving quotes: {ex.Message}",
                    "Data Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return new List<Quote>();
            }
        }

        public List<Quote> SearchQuotes(string searchTerm)
        {
            try
            {
                if (!_connector.Connect())
                {
                    return new List<Quote>();
                }

                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    return GetQuotes();
                }

                var query = new QueryExpression(_entityName)
                {
                    ColumnSet = new ColumnSet(
                        "name",
                        "quotenumber",
                        "customerid",
                        "statuscode"
                    ),
                    Orders = {
                        new OrderExpression("name", OrderType.Ascending)
                    }
                };

                // Create OR search conditions
                var searchGroup = new FilterExpression(LogicalOperator.Or);

                // Always search the full term against quote name
                searchGroup.AddCondition("name", ConditionOperator.Like, $"%{searchTerm}%");

                // Check if search term looks like a quote number
                var quoteNumberMatch = System.Text.RegularExpressions.Regex.Match(
                    searchTerm.Trim(),
                    @"^(QU|Q|)(\d+)"
                );

                if (quoteNumberMatch.Success)
                {
                    // Extract the quote number pattern
                    string quoteNumber = quoteNumberMatch.Groups[2].Value;

                    // Search for this number in the quote number field
                    searchGroup.AddCondition("quotenumber", ConditionOperator.Like, $"%{quoteNumber}%");
                    searchGroup.AddCondition("quotenumber", ConditionOperator.Like, $"%{searchTerm}%");
                }
                else
                {
                    // If it's not a numeric search, also search quote number field with full term
                    searchGroup.AddCondition("quotenumber", ConditionOperator.Like, $"%{searchTerm}%");
                }

                // Combine with status filter
                var combinedFilter = new FilterExpression(LogicalOperator.And);
                combinedFilter.AddFilter(searchGroup);

                // Exclude Won/Lost/Closed quotes
                combinedFilter.AddCondition("statuscode", ConditionOperator.NotIn, new object[] { 2, 3, 4 });

                query.Criteria = combinedFilter;

                // Page size for search results
                query.PageInfo = new PagingInfo
                {
                    Count = 1000,
                    PageNumber = 1
                };

                var result = _connector._orgService.RetrieveMultiple(query);

                if (result?.Entities == null)
                {
                    return new List<Quote>();
                }

                var quotes = result.Entities
                    .Select(Quote.FromEntity)
                    .Where(q => q != null && q.IsActive)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"SearchQuotes: Found {quotes.Count} quotes for term '{searchTerm}'");

                return quotes;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SearchQuotes error: {ex.Message}");
                MessageBox.Show($"Error searching quotes: {ex.Message}",
                    "Search Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return new List<Quote>();
            }
        }

        public Quote GetQuote(Guid id)
        {
            try
            {
                if (!_connector.Connect())
                {
                    return null;
                }

                var columns = new string[]
                {
                    "name",
                    "quotenumber",
                    "customerid",
                    "statuscode"
                };

                Entity entity = _connector.Retrieve(_entityName, id, columns);
                return Quote.FromEntity(entity);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving quote: {ex.Message}",
                    "Data Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }
    }
}