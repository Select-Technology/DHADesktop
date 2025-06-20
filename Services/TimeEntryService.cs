using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DHA.DSTC.WPF.Services
{
    public class TimeEntryService
    {
        private readonly DataverseConnector _connector;

        public TimeEntryService(DataverseConnector connector)
        {
            _connector = connector;
        }

        public List<TimeEntry> GetTimeEntries()
        {
            List<Entity> entities = _connector.RetrieveMultiple("fwp_timeentry");
            return entities.Select(TimeEntry.FromEntity).ToList();
        }

        public TimeEntry GetTimeEntry(Guid id)
        {
            Entity entity = _connector.Retrieve("fwp_timeentry", id);
            return TimeEntry.FromEntity(entity);
        }

        public Guid CreateTimeEntry(TimeEntry timeEntry)
        {
            Entity entity = timeEntry.ToEntity();
            return _connector.Create(entity);
        }

        public void UpdateTimeEntry(TimeEntry timeEntry)
        {
            Entity entity = timeEntry.ToEntity();
            _connector.Update(entity);
        }

        public void DeleteTimeEntry(Guid id)
        {
            _connector.Delete("fwp_timeentry", id);
        }
    }
}